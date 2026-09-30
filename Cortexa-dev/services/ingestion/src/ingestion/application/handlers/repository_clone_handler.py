import asyncio
import logging
from dataclasses import dataclass

from ingestion.application.dtos.github_scan_dtos import RepositoryTreeRequest
from ingestion.application.github_scan.clone_jobs import BackgroundRunner, CloneJobRegistry, utc_now
from ingestion.application.github_scan.clone_ports import CloneArchiveStore, RepositoryWorkspace
from ingestion.application.github_scan.github_port import GitHubRepositoryReader
from ingestion.application.github_scan.scan_target import (
    parse_owner,
    validate_branch,
    validate_owner,
    validate_repository,
    validate_user_id,
)
from ingestion.domain.enums.clone_status import CloneStatus
from ingestion.domain.errors.github_scan_errors import GitHubScanError, RepositoryTooLargeError
from ingestion.domain.models.repository_clone import (
    ArchiveDownload,
    RepositoryClone,
    clone_id_for,
)

_logger = logging.getLogger(__name__)

_BYTES_PER_KB = 1024
_BYTES_PER_MB = 1024 * 1024
_GENERIC_FAILURE = "Saving failed unexpectedly. Try again, or check the ingestion logs."


@dataclass(frozen=True)
class RepositoryCloneDeps:
    reader: GitHubRepositoryReader
    workspace: RepositoryWorkspace
    store: CloneArchiveStore
    registry: CloneJobRegistry
    runner: BackgroundRunner
    max_repo_bytes: int


class RepositoryCloneHandler:
    def __init__(self, deps: RepositoryCloneDeps) -> None:
        self._deps = deps

    async def start(self, request: RepositoryTreeRequest, user_id: str | None) -> RepositoryClone:
        user = validate_user_id(user_id)
        owner = parse_owner(request.org_url)
        repo = validate_repository(request.repository)
        branch = validate_branch(request.branch)
        clone_id = clone_id_for(owner, repo, branch)
        existing = self._deps.registry.active(clone_id)
        if existing is not None:
            return existing
        # The token stays in this call stack and the background task; it is never stored.
        token = request.pat.get_secret_value()
        await self._check_size(owner, repo, token)
        # Re-check after the await: a concurrent request may have queued the same branch meanwhile.
        existing = self._deps.registry.active(clone_id)
        if existing is not None:
            return existing
        clone = _new_clone(clone_id, user, SaveTarget(owner, repo, branch))
        self._deps.registry.put(clone)
        self._deps.runner.spawn(self._run(clone, token))
        _logger.info("Save %s queued for %s/%s@%s", clone_id, owner, repo, branch)
        return clone

    async def list_clones(self, user_id: str | None) -> list[RepositoryClone]:
        validate_user_id(user_id)
        by_id = {c.clone_id: c for c in await self._deps.store.list_clones()}
        # An in-progress or failed save replaces the older stored copy of the same branch.
        by_id.update({c.clone_id: c for c in self._deps.registry.all()})
        return sorted(by_id.values(), key=lambda c: c.updated_at, reverse=True)

    async def open_download(
        self, user_id: str | None, owner: str, repository: str, branch: str
    ) -> ArchiveDownload:
        validate_user_id(user_id)
        return await self._deps.store.open_download(
            validate_owner(owner), validate_repository(repository), validate_branch(branch)
        )

    async def _check_size(self, owner: str, repo: str, token: str) -> None:
        summary = await self._deps.reader.get_repository(owner, repo, token)
        size_bytes = summary.size_kb * _BYTES_PER_KB
        if size_bytes > self._deps.max_repo_bytes:
            raise RepositoryTooLargeError(
                f"{repo} is about {size_bytes // _BYTES_PER_MB} MB, above the "
                f"{self._deps.max_repo_bytes // _BYTES_PER_MB} MB limit for saving."
            )

    async def _run(self, clone: RepositoryClone, token: str) -> None:
        try:
            await self._clone_and_store(clone, token)
        except asyncio.CancelledError:
            raise
        except GitHubScanError as exc:
            self._fail(clone.clone_id, str(exc))
        except Exception:
            _logger.exception("Clone %s failed", clone.clone_id)
            self._fail(clone.clone_id, _GENERIC_FAILURE)

    async def _clone_and_store(self, clone: RepositoryClone, token: str) -> None:
        registry, workspace = self._deps.registry, self._deps.workspace
        registry.update(clone.clone_id, status=CloneStatus.CLONING)
        archive = await workspace.clone_and_pack(clone.owner, clone.repository, clone.branch, token)
        try:
            uploading = registry.update(
                clone.clone_id, status=CloneStatus.UPLOADING, commit_sha=archive.commit_sha
            )
            await self._deps.store.save(archive, uploading)
        finally:
            workspace.discard(archive)
        registry.remove(clone.clone_id)
        _logger.info("Clone %s stored", clone.clone_id)

    def _fail(self, clone_id: str, message: str) -> None:
        self._deps.registry.update(clone_id, status=CloneStatus.FAILED, error=message)
        _logger.warning("Clone %s failed: %s", clone_id, message)


@dataclass(frozen=True)
class SaveTarget:
    owner: str
    repository: str
    branch: str


def _new_clone(clone_id: str, user_id: str, target: SaveTarget) -> RepositoryClone:
    now = utc_now()
    return RepositoryClone(
        clone_id=clone_id,
        owner=target.owner,
        repository=target.repository,
        branch=target.branch,
        status=CloneStatus.QUEUED,
        created_at=now,
        updated_at=now,
        saved_by=user_id,
    )
