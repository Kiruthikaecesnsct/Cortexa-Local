import asyncio
import logging
from dataclasses import dataclass

from ingestion.application.repository_clone.clone_jobs import (
    BackgroundRunner,
    CloneJobRegistry,
    utc_now,
)
from ingestion.application.repository_clone.clone_ports import (
    CloneArchiveStore,
    RepositorySource,
    RepositoryWorkspace,
    SaveRequest,
)
from ingestion.application.repository_clone.user_context import validate_user_id
from ingestion.domain.enums.clone_status import CloneStatus
from ingestion.domain.errors.scan_errors import RepositoryTooLargeError, ScanError
from ingestion.domain.models.repository_clone import (
    ArchiveDownload,
    RepositoryClone,
    SaveTarget,
)

_logger = logging.getLogger(__name__)

_BYTES_PER_MB = 1024 * 1024
_GENERIC_FAILURE = "Saving failed unexpectedly. Try again, or check the ingestion logs."


@dataclass(frozen=True)
class RepositoryCloneDeps:
    source: RepositorySource
    workspace: RepositoryWorkspace
    store: CloneArchiveStore
    registry: CloneJobRegistry
    runner: BackgroundRunner
    max_repo_bytes: int


class RepositoryCloneHandler:
    """Saves branches from one provider; each provider gets its own handler instance."""

    def __init__(self, deps: RepositoryCloneDeps) -> None:
        self._deps = deps

    async def start(self, request: SaveRequest, user_id: str | None) -> RepositoryClone:
        user = validate_user_id(user_id)
        target = self._deps.source.target_from_request(request)
        existing = self._deps.registry.active(target.clone_id)
        if existing is not None:
            return existing
        # The token stays in this call stack and the background task; it is never stored.
        token = request.pat.get_secret_value()
        await self._check_size(target, token)
        # Re-check after the await: a concurrent request may have queued the same branch meanwhile.
        existing = self._deps.registry.active(target.clone_id)
        if existing is not None:
            return existing
        clone = _new_clone(target, user)
        self._deps.registry.put(clone)
        self._deps.runner.spawn(self._run(clone, token))
        _logger.info("Save %s queued for %s", clone.clone_id, _describe(target))
        return clone

    async def list_clones(self, user_id: str | None) -> list[RepositoryClone]:
        validate_user_id(user_id)
        provider = self._deps.source.provider
        by_id = {c.clone_id: c for c in await self._deps.store.list_clones(provider)}
        # An in-progress or failed save replaces the older stored copy of the same branch.
        by_id.update({c.clone_id: c for c in self._deps.registry.all() if c.provider == provider})
        return sorted(by_id.values(), key=lambda c: c.updated_at, reverse=True)

    async def open_download(
        self, user_id: str | None, owner: str, repository: str, branch: str
    ) -> ArchiveDownload:
        validate_user_id(user_id)
        target = self._deps.source.target_from_names(owner, repository, branch)
        return await self._deps.store.open_download(target)

    async def _check_size(self, target: SaveTarget, token: str) -> None:
        size_bytes = await self._deps.source.repository_size_bytes(target, token)
        if size_bytes > self._deps.max_repo_bytes:
            raise RepositoryTooLargeError(
                f"{target.repository_name} is about {size_bytes // _BYTES_PER_MB} MB, above the "
                f"{self._deps.max_repo_bytes // _BYTES_PER_MB} MB limit for saving."
            )

    async def _run(self, clone: RepositoryClone, token: str) -> None:
        try:
            await self._clone_and_store(clone, token)
        except asyncio.CancelledError:
            raise
        except ScanError as exc:
            self._fail(clone.clone_id, str(exc))
        except Exception:
            _logger.exception("Save %s failed", clone.clone_id)
            self._fail(clone.clone_id, _GENERIC_FAILURE)

    async def _clone_and_store(self, clone: RepositoryClone, token: str) -> None:
        registry, workspace = self._deps.registry, self._deps.workspace
        registry.update(clone.clone_id, status=CloneStatus.CLONING)
        archive = await workspace.clone_and_pack(
            self._deps.source.checkout_spec(clone.target, token)
        )
        try:
            uploading = registry.update(
                clone.clone_id, status=CloneStatus.UPLOADING, commit_sha=archive.commit_sha
            )
            await self._deps.store.save(archive, uploading)
        finally:
            workspace.discard(archive)
        registry.remove(clone.clone_id)
        _logger.info("Save %s stored", clone.clone_id)

    def _fail(self, clone_id: str, message: str) -> None:
        self._deps.registry.update(clone_id, status=CloneStatus.FAILED, error=message)
        _logger.warning("Save %s failed: %s", clone_id, message)


def _describe(target: SaveTarget) -> str:
    return f"{target.provider}:{target.owner}/{target.repository}@{target.branch}"


def _new_clone(target: SaveTarget, user_id: str) -> RepositoryClone:
    now = utc_now()
    return RepositoryClone(
        clone_id=target.clone_id,
        provider=target.provider,
        owner=target.owner,
        repository=target.repository,
        branch=target.branch,
        status=CloneStatus.QUEUED,
        created_at=now,
        updated_at=now,
        saved_by=user_id,
    )
