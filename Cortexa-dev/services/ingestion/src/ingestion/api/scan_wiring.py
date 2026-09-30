import logging
from dataclasses import dataclass

import httpx

from ingestion.application.github_scan.clone_jobs import BackgroundRunner, CloneJobRegistry
from ingestion.application.handlers.github_scan_handler import GitHubScanHandler
from ingestion.application.handlers.repository_clone_handler import (
    RepositoryCloneDeps,
    RepositoryCloneHandler,
)
from ingestion.domain.errors.github_scan_errors import GitHubScanError
from ingestion.infrastructure.config.settings import IngestionSettings
from ingestion.infrastructure.git.git_workspace import GitRepositoryWorkspace
from ingestion.infrastructure.github.github_api_client import (
    GitHubApiClient,
    create_github_http_client,
)
from ingestion.infrastructure.storage.s3_clone_store import build_clone_store

_logger = logging.getLogger(__name__)


@dataclass
class ScanServices:
    scan_handler: GitHubScanHandler
    clone_handler: RepositoryCloneHandler
    github_http: httpx.AsyncClient
    runner: BackgroundRunner

    async def aclose(self) -> None:
        await self.runner.aclose()
        await self.github_http.aclose()


async def build_scan_services(settings: IngestionSettings) -> ScanServices:
    github_http = create_github_http_client(settings)
    reader = GitHubApiClient(github_http, settings.github_scan_max_pages)
    store = build_clone_store(settings)
    try:
        await store.ensure_ready()
    except GitHubScanError as exc:
        # Storage being down must not stop ingestion; clone endpoints report it instead.
        _logger.warning("Clone storage unavailable at startup: %s", exc)
    runner = BackgroundRunner()
    deps = RepositoryCloneDeps(
        reader=reader,
        workspace=GitRepositoryWorkspace(settings),
        store=store,
        registry=CloneJobRegistry(),
        runner=runner,
        max_repo_bytes=settings.clone_max_repo_bytes,
    )
    return ScanServices(
        scan_handler=GitHubScanHandler(reader),
        clone_handler=RepositoryCloneHandler(deps),
        github_http=github_http,
        runner=runner,
    )
