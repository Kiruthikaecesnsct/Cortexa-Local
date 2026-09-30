import asyncio
import logging
import os
import tempfile
from pathlib import Path

from ingestion.domain.enums.git_host import GitHost
from ingestion.domain.errors.clone_errors import CloneTimeoutError, GitExecutionError
from ingestion.domain.errors.scan_errors import CloneExecutionError
from ingestion.domain.models.clone_request import RepoRef
from ingestion.domain.models.repository_clone import CheckoutSpec, PackedArchive
from ingestion.infrastructure.config.settings import IngestionSettings
from ingestion.infrastructure.git import git_runner
from ingestion.infrastructure.git.clone_cleanup import remove_clone

_logger = logging.getLogger(__name__)

_HOST_NAMES = {GitHost.GITHUB: "GitHub", GitHost.AZURE_DEVOPS: "Azure DevOps"}


class GitRepositoryWorkspace:
    """Shallow-clones one branch, then packs only tracked files with `git archive`.

    `git archive` stores symlinks as links and never follows them, so a hostile
    repository cannot pull files from this server into the archive.
    """

    def __init__(self, settings: IngestionSettings) -> None:
        self._settings = settings
        self._semaphore = asyncio.Semaphore(settings.clone_max_concurrency)

    async def clone_and_pack(self, spec: CheckoutSpec) -> PackedArchive:
        async with self._semaphore:
            checkout = await self._clone(spec)
            try:
                return await self._pack(checkout)
            finally:
                remove_clone(str(checkout))

    def discard(self, archive: PackedArchive) -> None:
        archive.path.unlink(missing_ok=True)

    async def _clone(self, spec: CheckoutSpec) -> Path:
        # git_runner only uses the URL; owner/repo are informational on this path.
        ref = RepoRef(host=spec.host, owner="", repo="", normalized_https_url=spec.url)
        host = _HOST_NAMES[spec.host]
        try:
            return Path(await git_runner.run_clone(ref, spec.token, self._settings, spec.branch))
        except CloneTimeoutError as exc:
            raise CloneExecutionError(
                f"Downloading from {host} timed out after {int(exc.timeout_seconds)} seconds."
            ) from exc
        except GitExecutionError as exc:
            _logger.warning("git clone failed for %s: %s", spec.url, exc)
            raise CloneExecutionError(
                f"Could not download the repository from {host}. Check that the token can read "
                "it and that the branch still exists."
            ) from exc

    async def _pack(self, checkout: Path) -> PackedArchive:
        commit_sha = (await self._git(checkout, "rev-parse", "HEAD")).strip()
        fd, zip_name = tempfile.mkstemp(suffix=".zip", dir=self._settings.clone_workdir)
        os.close(fd)
        archive = PackedArchive(path=Path(zip_name), commit_sha=commit_sha)
        try:
            await self._git(checkout, "archive", "--format=zip", "-o", zip_name, "HEAD")
        except BaseException:
            self.discard(archive)
            raise
        return archive

    async def _git(self, cwd: Path, *args: str) -> str:
        proc = await asyncio.create_subprocess_exec(
            "git",
            *args,
            cwd=str(cwd),
            stdout=asyncio.subprocess.PIPE,
            stderr=asyncio.subprocess.PIPE,
        )
        try:
            stdout, stderr = await asyncio.wait_for(
                proc.communicate(), timeout=self._settings.clone_timeout_seconds
            )
        except TimeoutError as exc:
            proc.kill()
            raise CloneExecutionError("Packing the repository timed out.") from exc
        if proc.returncode != 0:
            _logger.warning("git %s failed: %s", args[0], stderr.decode(errors="replace"))
            raise CloneExecutionError("Packing the repository failed.")
        return stdout.decode(errors="replace")
