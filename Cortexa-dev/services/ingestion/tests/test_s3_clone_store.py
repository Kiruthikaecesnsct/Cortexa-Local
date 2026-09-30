from datetime import UTC, datetime
from pathlib import Path
from unittest.mock import MagicMock

import pytest
from botocore.exceptions import ClientError, EndpointConnectionError

from ingestion.domain.enums.clone_status import CloneStatus
from ingestion.domain.enums.source_provider import SourceProvider
from ingestion.domain.errors.scan_errors import (
    CloneNotFoundError,
    CloneStorageUnavailableError,
)
from ingestion.domain.models.repository_clone import PackedArchive, RepositoryClone, SaveTarget
from ingestion.infrastructure.config.settings import IngestionSettings
from ingestion.infrastructure.storage.s3_clone_store import (
    S3CloneArchiveStore,
    UnconfiguredCloneStore,
    build_clone_store,
    object_key,
    target_from_key,
)

BUCKET = "cortexa"
USER = "user-1"
NOW = datetime(2026, 9, 30, 12, 0, tzinfo=UTC)
GITHUB = SourceProvider.GITHUB
AZURE = SourceProvider.AZURE_DEVOPS


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


def _client_error(code: str) -> ClientError:
    return ClientError({"Error": {"Code": code, "Message": code}}, "Op")


def _store(client: MagicMock, limit: int = 10) -> S3CloneArchiveStore:
    return S3CloneArchiveStore(client, BUCKET, limit)


def _obj(key: str, minute: int = 0, size: int = 1) -> dict:
    return {"Key": key, "LastModified": NOW.replace(minute=minute), "Size": size}


@pytest.mark.parametrize(
    ("target", "key"),
    [
        (SaveTarget(GITHUB, "acme", "api", "main"), "github/acme/api/main/api.zip"),
        (SaveTarget(GITHUB, "acme", "api", "feature/x"), "github/acme/api/feature/x/api.zip"),
        (
            SaveTarget(AZURE, "contoso", "Platform/api", "main"),
            "azure-devops/contoso/Platform/api/main/api.zip",
        ),
        (
            SaveTarget(AZURE, "contoso", "Platform/api", "release/1.0"),
            "azure-devops/contoso/Platform/api/release/1.0/api.zip",
        ),
    ],
)
def test_object_key_round_trips(target: SaveTarget, key: str) -> None:
    assert object_key(target) == key
    assert target_from_key(target.provider, key) == target


@pytest.mark.parametrize(
    ("provider", "key"),
    [
        (GITHUB, "github/acme/api/main/notes.txt"),
        (GITHUB, "github/stray.zip"),
        (AZURE, "azure-devops/contoso/Platform/api/api.zip"),
        (AZURE, "azure-devops/contoso/Platform/api/main/other.zip"),
    ],
)
def test_target_from_key_ignores_foreign_objects(provider: SourceProvider, key: str) -> None:
    assert target_from_key(provider, key) is None


async def test_save_uploads_to_provider_folder_with_metadata(tmp_path: Path) -> None:
    client = MagicMock()
    zip_path = tmp_path / "a.zip"
    zip_path.write_bytes(b"12345")
    target = SaveTarget(AZURE, "contoso", "Platform/api", "feature/x")

    size = await _store(client).save(PackedArchive(zip_path, "abcdef1234"), _clone(target))

    assert size == 5
    args, kwargs = client.upload_file.call_args
    assert args == (str(zip_path), BUCKET, "azure-devops/contoso/Platform/api/feature/x/api.zip")
    assert kwargs["ExtraArgs"]["Metadata"] == {
        "provider": "azure-devops",
        "owner": "contoso",
        "repository": "Platform/api",
        "branch": "feature/x",
        "commit-sha": "abcdef1234",
        "created-at": NOW.isoformat(),
        "saved-by": USER,
    }


async def test_list_reads_only_the_provider_folder_newest_first() -> None:
    client = MagicMock()
    client.get_paginator.return_value.paginate.return_value = [
        {
            "Contents": [
                _obj("github/acme/api/main/api.zip", minute=1, size=7),
                _obj("github/acme/api/feature/x/api.zip", minute=5, size=9),
                _obj("github/acme/api/main/notes.txt", minute=9),
            ]
        }
    ]
    client.head_object.return_value = {"Metadata": {"commit-sha": "abc"}}

    clones = await _store(client).list_clones(GITHUB)

    kwargs = client.get_paginator.return_value.paginate.call_args.kwargs
    assert (kwargs["Bucket"], kwargs["Prefix"]) == (BUCKET, "github/")
    assert [(c.owner, c.repository, c.branch, c.size_bytes) for c in clones] == [
        ("acme", "api", "feature/x", 9),
        ("acme", "api", "main", 7),
    ]
    assert clones[0].clone_id == SaveTarget(GITHUB, "acme", "api", "feature/x").clone_id
    assert clones[0].status == CloneStatus.STORED


async def test_list_respects_limit() -> None:
    client = MagicMock()
    objects = [_obj(f"github/acme/repo{i}/main/repo{i}.zip", minute=i) for i in range(5)]
    client.get_paginator.return_value.paginate.return_value = [{"Contents": objects}]
    client.head_object.return_value = {"Metadata": {}}

    clones = await _store(client, limit=2).list_clones(GITHUB)

    assert [c.repository for c in clones] == ["repo4", "repo3"]


async def test_open_download_streams_body_named_after_repo() -> None:
    client = MagicMock()
    body = MagicMock()
    body.iter_chunks.return_value = iter([b"ab", b"cd"])
    client.get_object.return_value = {"Body": body, "ContentLength": 4, "Metadata": {}}

    download = await _store(client).open_download(SaveTarget(AZURE, "contoso", "P/api", "main"))

    assert client.get_object.call_args.kwargs["Key"] == "azure-devops/contoso/P/api/main/api.zip"
    assert download.filename == "api.zip"
    assert b"".join(download.chunks) == b"abcd"
    body.close.assert_called_once()


@pytest.mark.parametrize(
    ("error", "expected"),
    [
        (_client_error("NoSuchKey"), CloneNotFoundError),
        (_client_error("AccessDenied"), CloneStorageUnavailableError),
        (EndpointConnectionError(endpoint_url="http://storage"), CloneStorageUnavailableError),
    ],
)
async def test_open_download_maps_storage_errors(error: Exception, expected: type) -> None:
    client = MagicMock()
    client.get_object.side_effect = error

    with pytest.raises(expected):
        await _store(client).open_download(SaveTarget(GITHUB, "acme", "api", "main"))


async def test_ensure_ready_creates_missing_bucket() -> None:
    client = MagicMock()
    client.head_bucket.side_effect = _client_error("404")

    await _store(client).ensure_ready()

    client.create_bucket.assert_called_once_with(Bucket=BUCKET)


async def test_ensure_ready_leaves_existing_bucket() -> None:
    client = MagicMock()

    await _store(client).ensure_ready()

    client.create_bucket.assert_not_called()


async def test_default_bucket_is_cortexa() -> None:
    assert IngestionSettings().s3_clone_bucket == "cortexa"


async def test_unconfigured_store_reports_unavailable() -> None:
    store = build_clone_store(IngestionSettings(s3_endpoint_url=""))

    assert isinstance(store, UnconfiguredCloneStore)
    with pytest.raises(CloneStorageUnavailableError):
        await store.list_clones(GITHUB)
