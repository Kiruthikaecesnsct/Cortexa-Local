import asyncio
import logging
import stat
from collections.abc import Iterator
from pathlib import Path

from ingestion.domain.enums.git_host import GitHost
from ingestion.domain.errors.clone_errors import CloneTimeoutError, GitExecutionError
from ingestion.domain.errors.scan_errors import CloneExecutionError
from ingestion.domain.models.clone_request import RepoRef
from ingestion.domain.models.repository_clone import CheckoutSpec, RepositoryCheckout
from ingestion.infrastructure.config.settings import IngestionSettings
from ingestion.infrastructure.git import git_runner
from ingestion.infrastructure.git.clone_cleanup import remove_clone

_logger = logging.getLogger(__name__)

_HOST_NAMES = {GitHost.GITHUB: "GitHub", GitHost.AZURE_DEVOPS: "Azure DevOps"}


def _regular_files(root: Path, listing: str) -> Iterator[str]:
    """Tracked entries that are plain files inside the checkout.

    Symlinks and submodule links are skipped, so a hostile repository cannot point
    the upload at files elsewhere on this server.
    """
    resolved_root = root.resolve()
    for name in filter(None, listing.split("\0")):
        if "\\" in name:
            continue  # Not a portable path; storage could not list it back.
        path = root / name
        try:
            mode = path.lstat().st_mode
        except OSError:
            continue
        if stat.S_ISREG(mode) and path.resolve().is_relative_to(resolved_root):
            yield name


class GitRepositoryWorkspace:
    """Shallow-clones one branch and lists the tracked files to save as a folder."""

    def __init__(self, settings: IngestionSettings) -> None:
        self._settings = settings
        self._semaphore = asyncio.Semaphore(settings.clone_max_concurrency)

    async def checkout(self, spec: CheckoutSpec) -> RepositoryCheckout:
        async with self._semaphore:
            path = await self._clone(spec)
            try:
                return await self._describe(path)
            except BaseException:
                remove_clone(str(path))
                raise

    def discard(self, checkout: RepositoryCheckout) -> None:
        remove_clone(str(checkout.path))

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

    async def _describe(self, path: Path) -> RepositoryCheckout:
        commit_sha = (await self._git(path, "rev-parse", "HEAD")).strip()
        listing = await self._git(path, "ls-files", "-z")
        return RepositoryCheckout(path, commit_sha, tuple(_regular_files(path, listing)))

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
            raise CloneExecutionError("Reading the repository timed out.") from exc
        if proc.returncode != 0:
            _logger.warning("git %s failed: %s", args[0], stderr.decode(errors="replace"))
            raise CloneExecutionError("Reading the repository failed.")
        return stdout.decode(errors="replace")
