import asyncio
from collections.abc import Iterator
from pathlib import Path

import pytest
from pydantic import SecretStr

from ingestion.application.dtos.azure_devops_scan_dtos import (
    RepositoryTreeRequest as AzureRequest,
)
from ingestion.application.dtos.github_scan_dtos import RepositoryTreeRequest as GitHubRequest
from ingestion.application.handlers.repository_clone_handler import (
    RepositoryCloneDeps,
    RepositoryCloneHandler,
)
from ingestion.application.repository_clone.azure_devops_source import AzureDevOpsSource
from ingestion.application.repository_clone.clone_jobs import BackgroundRunner, CloneJobRegistry
from ingestion.application.repository_clone.github_source import GitHubSource
from ingestion.domain.enums.clone_status import CloneStatus
from ingestion.domain.enums.source_provider import SourceProvider
from ingestion.domain.errors.github_scan_errors import InvalidScanTargetError
from ingestion.domain.errors.scan_errors import (
    CloneExecutionError,
    CloneNotFoundError,
    MissingUserContextError,
    RepositoryTooLargeError,
)
from ingestion.domain.models.azure_devops_scan import RepositorySummary as AzureSummary
from ingestion.domain.models.github_scan import RepositorySummary as GitHubSummary
from ingestion.domain.models.repository_clone import (
    ArchiveDownload,
    CheckoutSpec,
    RepositoryCheckout,
    RepositoryClone,
    SaveTarget,
)

TOKEN = "ghp_supersecrettoken"
USER = "user-1"
OTHER_USER = "user-2"
MAX_BYTES = 10 * 1024 * 1024
SETTLE_ROUNDS = 20
CHECKOUT = RepositoryCheckout(Path("checkout"), "abc1234def", ("README.md",))


def _github_request(branch: str = "main") -> GitHubRequest:
    return GitHubRequest(
        org_url="https://github.com/acme", pat=SecretStr(TOKEN), repository="api", branch=branch
    )


def _azure_request(branch: str = "main") -> AzureRequest:
    return AzureRequest(
        org_url="https://dev.azure.com/contoso",
        pat=SecretStr(TOKEN),
        repository="Platform/api",
        branch=branch,
    )


class FakeGitHubReader:
    def __init__(self, size_kb: int = 100) -> None:
        self.size_kb = size_kb

    async def get_repository(self, owner: str, repo: str, token: str) -> GitHubSummary:
        return GitHubSummary(
            name=repo,
            full_name=f"{owner}/{repo}",
            private=True,
            default_branch="main",
            html_url="x",
            size_kb=self.size_kb,
        )


class FakeAzureReader:
    async def get_repository(
        self, organization: str, project: str, repository: str, token: str
    ) -> AzureSummary:
        return AzureSummary(name=f"{project}/{repository}", full_name="x", html_url="x", size_kb=1)


class FakeWorkspace:
    def __init__(self, error: Exception | None = None) -> None:
        self.error = error
        self.specs: list[CheckoutSpec] = []
        self.discarded: list[RepositoryCheckout] = []

    async def checkout(self, spec: CheckoutSpec) -> RepositoryCheckout:
        self.specs.append(spec)
        if self.error:
            raise self.error
        return CHECKOUT

    def discard(self, checkout: RepositoryCheckout) -> None:
        self.discarded.append(checkout)


class FakeStore:
    """Keyed like the real store: one object per saved target."""

    def __init__(self) -> None:
        self.saved: dict[SaveTarget, RepositoryClone] = {}

    async def ensure_ready(self) -> None:
        return None

    async def save(self, checkout: RepositoryCheckout, clone: RepositoryClone) -> int:
        stored = clone.model_copy(update={"status": CloneStatus.STORED, "size_bytes": 42})
        self.saved[clone.target] = stored
        return 42

    async def list_clones(self, provider: SourceProvider) -> list[RepositoryClone]:
        return [c for c in self.saved.values() if c.provider == provider]

    async def open_download(self, target: SaveTarget) -> ArchiveDownload:
        if target not in self.saved:
            raise CloneNotFoundError("Saved repository not found.")
        chunks: Iterator[bytes] = iter([b"zip"])
        return ArchiveDownload(
            chunks=chunks, size_bytes=3, filename=f"{target.repository_name}.zip"
        )


class Harness:
    """GitHub and Azure DevOps handlers sharing one store, registry and runner, as in wiring."""

    def __init__(self, workspace: FakeWorkspace | None = None, github_size_kb: int = 100) -> None:
        self.workspace = workspace or FakeWorkspace()
        self.store = FakeStore()
        self.registry = CloneJobRegistry()
        self.runner = BackgroundRunner()
        self.github = self._handler(
            GitHubSource(FakeGitHubReader(github_size_kb), "https://github.com")
        )
        self.azure = self._handler(AzureDevOpsSource(FakeAzureReader(), "https://dev.azure.com"))

    def _handler(self, source) -> RepositoryCloneHandler:  # noqa: ANN001
        return RepositoryCloneHandler(
            RepositoryCloneDeps(
                source=source,
                workspace=self.workspace,
                store=self.store,
                registry=self.registry,
                runner=self.runner,
                max_repo_bytes=MAX_BYTES,
            )
        )


async def _settle() -> None:
    for _ in range(SETTLE_ROUNDS):
        await asyncio.sleep(0)


async def test_github_save_queues_then_stores_the_folder() -> None:
    h = Harness()

    queued = await h.github.start(_github_request(), USER)
    assert (queued.status, queued.provider) == (CloneStatus.QUEUED, SourceProvider.GITHUB)
    await _settle()

    [clone] = await h.github.list_clones(USER)
    assert (clone.clone_id, clone.status, clone.commit_sha) == (
        queued.clone_id,
        CloneStatus.STORED,
        "abc1234def",
    )
    assert h.registry.all() == []
    assert h.workspace.discarded == [CHECKOUT]
    assert h.workspace.specs[0].url == "https://github.com/acme/api.git"


async def test_azure_save_uses_azure_clone_url_and_project_repository() -> None:
    h = Harness()

    queued = await h.azure.start(_azure_request(), USER)
    await _settle()

    assert (queued.provider, queued.owner, queued.repository) == (
        SourceProvider.AZURE_DEVOPS,
        "contoso",
        "Platform/api",
    )
    assert h.workspace.specs[0].url == "https://dev.azure.com/contoso/Platform/_git/api"
    assert [c.status for c in await h.azure.list_clones(USER)] == [CloneStatus.STORED]


async def test_each_provider_lists_only_its_own_saves() -> None:
    h = Harness()
    await h.github.start(_github_request(), USER)
    await h.azure.start(_azure_request(), USER)
    await _settle()

    assert [c.provider for c in await h.github.list_clones(USER)] == [SourceProvider.GITHUB]
    assert [c.provider for c in await h.azure.list_clones(USER)] == [SourceProvider.AZURE_DEVOPS]


async def test_token_reaches_git_but_never_the_clone_record() -> None:
    h = Harness()

    queued = await h.github.start(_github_request(), USER)
    await _settle()

    assert h.workspace.specs[0].token == TOKEN
    assert TOKEN not in repr(h.workspace.specs[0])
    for clone in [queued, *await h.github.list_clones(USER)]:
        assert TOKEN not in clone.model_dump_json()
        assert "saved_by" not in clone.model_dump()


async def test_saves_are_shared_between_signed_in_users() -> None:
    h = Harness()
    await h.azure.start(_azure_request(), USER)
    await _settle()

    clones = await h.azure.list_clones(OTHER_USER)
    download = await h.azure.open_download(OTHER_USER, "contoso", "Platform/api", "main")

    assert [c.repository for c in clones] == ["Platform/api"]
    assert download.filename == "api.zip"


async def test_missing_user_is_rejected_before_any_work() -> None:
    h = Harness()

    with pytest.raises(MissingUserContextError):
        await h.github.start(_github_request(), None)
    with pytest.raises(MissingUserContextError):
        await h.azure.list_clones("bad id!")
    with pytest.raises(MissingUserContextError):
        await h.github.open_download(None, "acme", "api", "main")
    assert h.runner.active_count == 0


async def test_too_large_repository_is_rejected() -> None:
    h = Harness(github_size_kb=MAX_BYTES)

    with pytest.raises(RepositoryTooLargeError):
        await h.github.start(_github_request(), USER)
    assert h.registry.all() == []


async def test_concurrent_requests_for_same_branch_start_one_save() -> None:
    h = Harness()

    first, second = await asyncio.gather(
        h.github.start(_github_request(), USER), h.github.start(_github_request(), OTHER_USER)
    )

    assert first.clone_id == second.clone_id
    await _settle()
    assert len(h.workspace.specs) == 1


async def test_same_names_on_different_providers_are_separate_saves() -> None:
    h = Harness()

    github = await h.github.start(_github_request(), USER)
    azure = await h.azure.start(_azure_request(), USER)

    assert github.clone_id != azure.clone_id
    await _settle()


async def test_clone_error_marks_job_failed_with_its_message() -> None:
    h = Harness(FakeWorkspace(CloneExecutionError("Could not download.")))

    await h.azure.start(_azure_request(), USER)
    await _settle()

    [clone] = await h.azure.list_clones(USER)
    assert (clone.status, clone.error) == (CloneStatus.FAILED, "Could not download.")


async def test_unexpected_error_shows_generic_message() -> None:
    h = Harness(FakeWorkspace(RuntimeError(f"boom {TOKEN}")))

    await h.github.start(_github_request(), USER)
    await _settle()

    [clone] = await h.github.list_clones(USER)
    assert clone.status == CloneStatus.FAILED
    assert TOKEN not in (clone.error or "")


async def test_retry_replaces_the_earlier_failed_attempt() -> None:
    workspace = FakeWorkspace(CloneExecutionError("Could not download."))
    h = Harness(workspace)
    await h.github.start(_github_request(), USER)
    await _settle()

    workspace.error = None
    retry = await h.github.start(_github_request(), USER)
    await _settle()

    clones = await h.github.list_clones(USER)
    assert [(c.clone_id, c.status) for c in clones] == [(retry.clone_id, CloneStatus.STORED)]


async def test_in_progress_save_replaces_stored_copy_in_list() -> None:
    h = Harness()
    await h.github.start(_github_request(), USER)
    await _settle()

    await h.github.start(_github_request(), USER)
    [clone] = await h.github.list_clones(USER)

    assert clone.status == CloneStatus.QUEUED
    await _settle()


async def test_download_validates_names_and_reports_missing() -> None:
    h = Harness()

    with pytest.raises(CloneNotFoundError):
        await h.github.open_download(USER, "acme", "api", "main")
    with pytest.raises(InvalidScanTargetError):
        await h.github.open_download(USER, "..", "api", "main")
    with pytest.raises(InvalidScanTargetError):
        await h.github.open_download(USER, "acme", "api", "../../etc")


async def test_failed_upload_still_discards_the_checkout() -> None:
    h = Harness()

    async def failing_save(checkout: RepositoryCheckout, clone: RepositoryClone) -> int:
        raise CloneExecutionError("Upload failed.")

    h.store.save = failing_save  # type: ignore[method-assign]
    await h.github.start(_github_request(), USER)
    await _settle()

    [clone] = await h.github.list_clones(USER)
    assert (clone.status, clone.error) == (CloneStatus.FAILED, "Upload failed.")
    assert h.workspace.discarded == [CHECKOUT]
