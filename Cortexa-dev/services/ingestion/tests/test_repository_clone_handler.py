import asyncio
from collections.abc import Iterator
from pathlib import Path

import pytest
from pydantic import SecretStr

from ingestion.application.dtos.github_scan_dtos import RepositoryTreeRequest
from ingestion.application.github_scan.clone_jobs import BackgroundRunner, CloneJobRegistry
from ingestion.application.handlers.repository_clone_handler import (
    RepositoryCloneDeps,
    RepositoryCloneHandler,
)
from ingestion.domain.enums.clone_status import CloneStatus
from ingestion.domain.errors.github_scan_errors import (
    CloneExecutionError,
    CloneNotFoundError,
    InvalidScanTargetError,
    MissingUserContextError,
    RepositoryTooLargeError,
)
from ingestion.domain.models.github_scan import RepositorySummary
from ingestion.domain.models.repository_clone import (
    ArchiveDownload,
    PackedArchive,
    RepositoryClone,
    clone_id_for,
)

TOKEN = "ghp_supersecrettoken"
USER = "user-1"
OTHER_USER = "user-2"
MAX_BYTES = 10 * 1024 * 1024
SETTLE_ROUNDS = 20


def _request(branch: str = "main") -> RepositoryTreeRequest:
    return RepositoryTreeRequest(
        org_url="https://github.com/acme", pat=SecretStr(TOKEN), repository="api", branch=branch
    )


class FakeReader:
    def __init__(self, size_kb: int = 100) -> None:
        self.size_kb = size_kb

    async def get_repository(self, owner: str, repo: str, token: str) -> RepositorySummary:
        return RepositorySummary(
            name=repo,
            full_name=f"{owner}/{repo}",
            private=True,
            default_branch="main",
            html_url="x",
            size_kb=self.size_kb,
        )


class FakeWorkspace:
    def __init__(self, error: Exception | None = None) -> None:
        self.error = error
        self.tokens: list[str] = []
        self.discarded: list[PackedArchive] = []

    async def clone_and_pack(self, owner: str, repo: str, branch: str, token: str) -> PackedArchive:
        self.tokens.append(token)
        if self.error:
            raise self.error
        return PackedArchive(path=Path("fake.zip"), commit_sha="abc1234def")

    def discard(self, archive: PackedArchive) -> None:
        self.discarded.append(archive)


class FakeStore:
    """Keyed like the real store: one object per owner/repository/branch."""

    def __init__(self) -> None:
        self.saved: dict[tuple[str, str, str], RepositoryClone] = {}

    async def ensure_ready(self) -> None:
        return None

    async def save(self, archive: PackedArchive, clone: RepositoryClone) -> int:
        stored = clone.model_copy(update={"status": CloneStatus.STORED, "size_bytes": 42})
        self.saved[(clone.owner, clone.repository, clone.branch)] = stored
        return 42

    async def list_clones(self) -> list[RepositoryClone]:
        return list(self.saved.values())

    async def open_download(self, owner: str, repository: str, branch: str) -> ArchiveDownload:
        if (owner, repository, branch) not in self.saved:
            raise CloneNotFoundError("Saved repository not found.")
        chunks: Iterator[bytes] = iter([b"zip"])
        return ArchiveDownload(chunks=chunks, size_bytes=3, filename=f"{repository}.zip")


def _handler(
    workspace: FakeWorkspace | None = None, reader: FakeReader | None = None
) -> tuple[RepositoryCloneHandler, RepositoryCloneDeps]:
    deps = RepositoryCloneDeps(
        reader=reader or FakeReader(),
        workspace=workspace or FakeWorkspace(),
        store=FakeStore(),
        registry=CloneJobRegistry(),
        runner=BackgroundRunner(),
        max_repo_bytes=MAX_BYTES,
    )
    return RepositoryCloneHandler(deps), deps


async def _settle() -> None:
    for _ in range(SETTLE_ROUNDS):
        await asyncio.sleep(0)


async def test_start_queues_then_stores_the_archive() -> None:
    handler, deps = _handler()

    queued = await handler.start(_request(), USER)
    assert queued.status == CloneStatus.QUEUED
    assert queued.clone_id == clone_id_for("acme", "api", "main")
    await _settle()

    clones = await handler.list_clones(USER)
    assert [(c.clone_id, c.status, c.commit_sha) for c in clones] == [
        (queued.clone_id, CloneStatus.STORED, "abc1234def")
    ]
    assert deps.registry.all() == []
    assert deps.workspace.discarded == [PackedArchive(Path("fake.zip"), "abc1234def")]


async def test_token_reaches_git_but_never_the_clone_record() -> None:
    workspace = FakeWorkspace()
    handler, _ = _handler(workspace)

    queued = await handler.start(_request(), USER)
    await _settle()

    assert workspace.tokens == [TOKEN]
    for clone in [queued, *await handler.list_clones(USER)]:
        assert TOKEN not in clone.model_dump_json()
        assert "saved_by" not in clone.model_dump()


async def test_saves_are_shared_between_signed_in_users() -> None:
    handler, _ = _handler()
    await handler.start(_request(), USER)
    await _settle()

    clones = await handler.list_clones(OTHER_USER)
    download = await handler.open_download(OTHER_USER, "acme", "api", "main")

    assert [c.repository for c in clones] == ["api"]
    assert download.filename == "api.zip"


async def test_missing_user_is_rejected_before_any_work() -> None:
    handler, deps = _handler()

    with pytest.raises(MissingUserContextError):
        await handler.start(_request(), None)
    with pytest.raises(MissingUserContextError):
        await handler.list_clones("bad id!")
    with pytest.raises(MissingUserContextError):
        await handler.open_download(None, "acme", "api", "main")
    assert deps.runner.active_count == 0


async def test_too_large_repository_is_rejected() -> None:
    handler, deps = _handler(reader=FakeReader(size_kb=MAX_BYTES))

    with pytest.raises(RepositoryTooLargeError):
        await handler.start(_request(), USER)
    assert deps.registry.all() == []


async def test_second_request_for_same_branch_reuses_active_save() -> None:
    handler, _ = _handler()

    first = await handler.start(_request(), USER)
    second = await handler.start(_request(), OTHER_USER)

    assert second is first
    await _settle()


async def test_concurrent_requests_for_same_branch_start_one_save() -> None:
    workspace = FakeWorkspace()
    handler, _ = _handler(workspace)

    first, second = await asyncio.gather(
        handler.start(_request(), USER), handler.start(_request(), USER)
    )

    assert first.clone_id == second.clone_id
    await _settle()
    assert len(workspace.tokens) == 1


async def test_clone_error_marks_job_failed_with_its_message() -> None:
    handler, _ = _handler(FakeWorkspace(CloneExecutionError("Could not download.")))

    await handler.start(_request(), USER)
    await _settle()

    [clone] = await handler.list_clones(USER)
    assert (clone.status, clone.error) == (CloneStatus.FAILED, "Could not download.")


async def test_unexpected_error_shows_generic_message() -> None:
    handler, _ = _handler(FakeWorkspace(RuntimeError(f"boom {TOKEN}")))

    await handler.start(_request(), USER)
    await _settle()

    [clone] = await handler.list_clones(USER)
    assert clone.status == CloneStatus.FAILED
    assert TOKEN not in (clone.error or "")


async def test_retry_replaces_the_earlier_failed_attempt() -> None:
    workspace = FakeWorkspace(CloneExecutionError("Could not download."))
    handler, _ = _handler(workspace)
    await handler.start(_request(), USER)
    await _settle()

    workspace.error = None
    retry = await handler.start(_request(), USER)
    await _settle()

    clones = await handler.list_clones(USER)
    assert [(c.clone_id, c.status) for c in clones] == [(retry.clone_id, CloneStatus.STORED)]


async def test_saving_same_branch_again_keeps_one_entry() -> None:
    handler, _ = _handler()
    await handler.start(_request(), USER)
    await _settle()
    await handler.start(_request(), USER)
    await _settle()

    assert len(await handler.list_clones(USER)) == 1


async def test_in_progress_save_replaces_stored_copy_in_list() -> None:
    handler, _ = _handler()
    await handler.start(_request(), USER)
    await _settle()

    await handler.start(_request(), USER)
    [clone] = await handler.list_clones(USER)

    assert clone.status == CloneStatus.QUEUED
    await _settle()


async def test_different_branches_are_listed_separately() -> None:
    handler, _ = _handler()
    await handler.start(_request("main"), USER)
    await handler.start(_request("dev"), USER)
    await _settle()

    assert sorted(c.branch for c in await handler.list_clones(USER)) == ["dev", "main"]


async def test_download_validates_names_and_reports_missing() -> None:
    handler, _ = _handler()

    with pytest.raises(CloneNotFoundError):
        await handler.open_download(USER, "acme", "api", "main")
    with pytest.raises(InvalidScanTargetError):
        await handler.open_download(USER, "..", "api", "main")
    with pytest.raises(InvalidScanTargetError):
        await handler.open_download(USER, "acme", "api", "../../etc")
