import io
import json
import zipfile
from datetime import UTC, datetime, timedelta
from pathlib import Path
from typing import Any
from unittest.mock import MagicMock

import pytest
from botocore.exceptions import ClientError, EndpointConnectionError

from ingestion.domain.enums.clone_status import CloneStatus
from ingestion.domain.enums.source_provider import SourceProvider
from ingestion.domain.errors.scan_errors import (
    CloneNotFoundError,
    CloneStorageUnavailableError,
)
from ingestion.domain.models.repository_clone import (
    RepositoryCheckout,
    RepositoryClone,
    SaveTarget,
)
from ingestion.infrastructure.config.settings import IngestionSettings
from ingestion.infrastructure.storage.clone_keys import (
    local_path,
    marker_key,
    safe_relative_path,
    target_from_marker_key,
)
from ingestion.infrastructure.storage.s3_clone_store import (
    CloneStoreOptions,
    S3CloneFolderStore,
    UnconfiguredCloneStore,
    build_clone_store,
)

BUCKET = "cortexa"
USER = "user-1"
NOW = datetime(2026, 9, 30, 12, 0, tzinfo=UTC)
GITHUB = SourceProvider.GITHUB
AZURE = SourceProvider.AZURE_DEVOPS
MAIN = SaveTarget(GITHUB, "acme", "api", "main")


def _client_error(code: str) -> ClientError:
    return ClientError({"Error": {"Code": code, "Message": code}}, "Op")


class FakeS3:
    """Just enough of the boto3 S3 client to exercise the store end to end."""

    def __init__(self) -> None:
        self.objects: dict[str, dict[str, Any]] = {}
        self._clock = NOW

    def _put(self, key: str, data: bytes, metadata: dict[str, str] | None = None) -> None:
        self._clock += timedelta(minutes=1)
        self.objects[key] = {"data": data, "meta": metadata or {}, "modified": self._clock}

    def _get(self, key: str) -> dict[str, Any]:
        if key not in self.objects:
            raise _client_error("NoSuchKey")
        return self.objects[key]

    def upload_file(self, path: str, bucket: str, key: str, ExtraArgs: dict) -> None:  # noqa: N803
        self._put(key, Path(path).read_bytes())

    def put_object(
        self, Bucket: str, Key: str, Body: bytes, ContentType: str, Metadata: dict
    ) -> None:  # noqa: N803
        self._put(Key, Body, Metadata)

    def get_object(self, Bucket: str, Key: str) -> dict:  # noqa: N803
        return {"Body": io.BytesIO(self._get(Key)["data"])}

    def head_object(self, Bucket: str, Key: str) -> dict:  # noqa: N803
        return {"Metadata": self._get(Key)["meta"]}

    def download_file(self, bucket: str, key: str, path: str) -> None:
        Path(path).write_bytes(self._get(key)["data"])

    def delete_objects(self, Bucket: str, Delete: dict) -> None:  # noqa: N803
        for item in Delete["Objects"]:
            self.objects.pop(item["Key"], None)

    def get_paginator(self, name: str) -> MagicMock:
        def paginate(Bucket: str, Prefix: str, PaginationConfig: dict) -> list[dict]:  # noqa: N803
            contents = [
                {"Key": key, "LastModified": obj["modified"], "Size": len(obj["data"])}
                for key, obj in self.objects.items()
                if key.startswith(Prefix)
            ]
            return [{"Contents": contents}]

        paginator = MagicMock()
        paginator.paginate.side_effect = paginate
        return paginator


def _clone(target: SaveTarget) -> RepositoryClone:
    return RepositoryClone(
        clone_id=target.clone_id,
        provider=target.provider,
        owner=target.owner,
        repository=target.repository,
        branch=target.branch,
        status=CloneStatus.UPLOADING,
        created_at=NOW,
        updated_at=NOW,
        commit_sha="abcdef1234",
        saved_by=USER,
    )


def _checkout(root: Path, files: dict[str, str]) -> RepositoryCheckout:
    for name, text in files.items():
        (root / name).parent.mkdir(parents=True, exist_ok=True)
        (root / name).write_text(text)
    return RepositoryCheckout(root, "abcdef1234", tuple(files))


def _store(client: Any, workdir: Path, limit: int = 10) -> S3CloneFolderStore:
    return S3CloneFolderStore(client, CloneStoreOptions(BUCKET, limit, str(workdir), 4))


@pytest.mark.parametrize(
    ("target", "key"),
    [
        (MAIN, "_saved/github/acme/api/main.json"),
        (SaveTarget(GITHUB, "acme", "api", "feature/x"), "_saved/github/acme/api/feature/x.json"),
        (
            SaveTarget(AZURE, "contoso", "Platform/api", "release/1.0"),
            "_saved/azure-devops/contoso/Platform/api/release/1.0.json",
        ),
    ],
)
def test_marker_key_round_trips(target: SaveTarget, key: str) -> None:
    assert marker_key(target) == key
    assert target_from_marker_key(target.provider, key) == target


@pytest.mark.parametrize(
    ("provider", "key"),
    [
        (GITHUB, "github/acme/api/main/README.md"),
        (GITHUB, "_saved/github/acme/main.json"),
        (GITHUB, "_saved/github/acme/api/main.txt"),
        (AZURE, "_saved/azure-devops/contoso/Platform/main.json"),
    ],
)
def test_target_from_marker_key_ignores_other_objects(provider: SourceProvider, key: str) -> None:
    assert target_from_marker_key(provider, key) is None


@pytest.mark.parametrize("name", ["", "/etc/passwd", "../x", "a/../../x", "a\\..\\x"])
def test_unsafe_manifest_paths_are_rejected(name: str, tmp_path: Path) -> None:
    assert safe_relative_path(name) is None
    assert local_path(tmp_path, name) is None


async def test_save_stores_the_exact_folder_and_a_marker(tmp_path: Path) -> None:
    client = FakeS3()
    checkout = _checkout(tmp_path / "co", {"README.md": "hi", "src/app.py": "print(1)"})

    size = await _store(client, tmp_path).save(checkout, _clone(MAIN))

    assert size == len("hi") + len("print(1)")
    assert client.objects["github/acme/api/main/README.md"]["data"] == b"hi"
    assert client.objects["github/acme/api/main/src/app.py"]["data"] == b"print(1)"
    marker = client.objects["_saved/github/acme/api/main.json"]
    assert json.loads(marker["data"]) == {"files": ["README.md", "src/app.py"]}
    assert marker["meta"] == {
        "provider": "github",
        "owner": "acme",
        "repository": "api",
        "branch": "main",
        "commit-sha": "abcdef1234",
        "created-at": NOW.isoformat(),
        "saved-by": USER,
        "total-bytes": str(size),
    }


async def test_save_again_removes_stale_files_but_not_other_branches(tmp_path: Path) -> None:
    client = FakeS3()
    store = _store(client, tmp_path)
    nested = SaveTarget(GITHUB, "acme", "api", "main/x")
    await store.save(_checkout(tmp_path / "a", {"old.txt": "o", "keep.txt": "k"}), _clone(MAIN))
    await store.save(_checkout(tmp_path / "b", {"n.txt": "n"}), _clone(nested))
    client._put("github/acme/api/main/api.zip", b"legacy")

    await store.save(_checkout(tmp_path / "c", {"keep.txt": "k2"}), _clone(MAIN))

    keys = set(client.objects)
    assert "github/acme/api/main/old.txt" not in keys
    assert "github/acme/api/main/api.zip" not in keys
    assert client.objects["github/acme/api/main/keep.txt"]["data"] == b"k2"
    assert "github/acme/api/main/x/n.txt" in keys


async def test_list_reads_markers_newest_first_with_size(tmp_path: Path) -> None:
    client = FakeS3()
    store = _store(client, tmp_path)
    feature = SaveTarget(GITHUB, "acme", "api", "feature/x")
    await store.save(_checkout(tmp_path / "a", {"a.txt": "1234567"}), _clone(MAIN))
    await store.save(_checkout(tmp_path / "b", {"b.txt": "123456789"}), _clone(feature))
    client._put("github/acme/stray.zip", b"x")

    clones = await store.list_clones(GITHUB)

    assert [(c.branch, c.size_bytes, c.status) for c in clones] == [
        ("feature/x", 9, CloneStatus.STORED),
        ("main", 7, CloneStatus.STORED),
    ]
    assert clones[0].clone_id == feature.clone_id
    assert await store.list_clones(AZURE) == []


async def test_list_respects_limit(tmp_path: Path) -> None:
    client = FakeS3()
    store = _store(client, tmp_path, limit=2)
    for i in range(4):
        target = SaveTarget(GITHUB, "acme", f"repo{i}", "main")
        await store.save(_checkout(tmp_path / str(i), {"f.txt": "x"}), _clone(target))

    clones = await store.list_clones(GITHUB)

    assert [c.repository for c in clones] == ["repo3", "repo2"]


async def test_fetch_folder_recreates_files_and_skips_unsafe_entries(tmp_path: Path) -> None:
    client = FakeS3()
    store = _store(client, tmp_path)
    await store.save(_checkout(tmp_path / "co", {"src/app.py": "print(1)"}), _clone(MAIN))
    client._put(marker_key(MAIN), json.dumps({"files": ["src/app.py", "../evil"]}).encode())
    destination = tmp_path / "dest"
    destination.mkdir()

    await store.fetch_folder(MAIN, destination)

    assert (destination / "src" / "app.py").read_text() == "print(1)"
    assert not (tmp_path / "evil").exists()


async def test_open_download_zips_the_folder_and_cleans_up(tmp_path: Path) -> None:
    client = FakeS3()
    workdir = tmp_path / "work"
    workdir.mkdir()
    store = _store(client, workdir)
    await store.save(_checkout(tmp_path / "co", {"a.txt": "A", "d/b.txt": "B"}), _clone(MAIN))

    download = await store.open_download(MAIN)
    data = b"".join(download.chunks)

    assert download.filename == "api.zip"
    assert download.size_bytes == len(data)
    with zipfile.ZipFile(io.BytesIO(data)) as archive:
        assert sorted(archive.namelist()) == ["a.txt", "d/b.txt"]
        assert archive.read("d/b.txt") == b"B"
    assert list(workdir.iterdir()) == []


@pytest.mark.parametrize(
    ("error", "expected"),
    [
        (_client_error("NoSuchKey"), CloneNotFoundError),
        (_client_error("AccessDenied"), CloneStorageUnavailableError),
        (EndpointConnectionError(endpoint_url="http://storage"), CloneStorageUnavailableError),
    ],
)
async def test_storage_errors_are_mapped(error: Exception, expected: type, tmp_path: Path) -> None:
    client = MagicMock()
    client.get_object.side_effect = error
    store = _store(client, tmp_path)

    with pytest.raises(expected):
        await store.open_download(MAIN)
    with pytest.raises(expected):
        await store.fetch_folder(MAIN, tmp_path)


async def test_ensure_ready_creates_missing_bucket(tmp_path: Path) -> None:
    client = MagicMock()
    client.head_bucket.side_effect = _client_error("404")

    await _store(client, tmp_path).ensure_ready()

    client.create_bucket.assert_called_once_with(Bucket=BUCKET)


async def test_ensure_ready_leaves_existing_bucket(tmp_path: Path) -> None:
    client = MagicMock()

    await _store(client, tmp_path).ensure_ready()

    client.create_bucket.assert_not_called()


async def test_default_bucket_is_cortexa() -> None:
    assert IngestionSettings().s3_clone_bucket == "cortexa"


async def test_unconfigured_store_reports_unavailable(tmp_path: Path) -> None:
    store = build_clone_store(IngestionSettings(s3_endpoint_url=""))

    assert isinstance(store, UnconfiguredCloneStore)
    with pytest.raises(CloneStorageUnavailableError):
        await store.list_clones(GITHUB)
    with pytest.raises(CloneStorageUnavailableError):
        await store.fetch_folder(MAIN, tmp_path)


async def test_damaged_marker_is_treated_as_empty_and_save_rewrites_it(tmp_path: Path) -> None:
    client = FakeS3()
    client._put(marker_key(MAIN), b"not json")
    store = _store(client, tmp_path)

    await store.save(_checkout(tmp_path / "co", {"a.txt": "A"}), _clone(MAIN))

    assert json.loads(client.objects[marker_key(MAIN)]["data"]) == {"files": ["a.txt"]}
