import logging
from dataclasses import dataclass

import httpx

from ingestion.application.handlers.azure_devops_scan_handler import AzureDevOpsScanHandler
from ingestion.application.handlers.github_scan_handler import GitHubScanHandler
from ingestion.application.handlers.repository_clone_handler import (
    RepositoryCloneDeps,
    RepositoryCloneHandler,
)
from ingestion.application.repository_clone.azure_devops_source import AzureDevOpsSource
from ingestion.application.repository_clone.clone_jobs import BackgroundRunner, CloneJobRegistry
from ingestion.application.repository_clone.clone_ports import (
    CloneFolderStore,
    RepositorySource,
)
from ingestion.application.repository_clone.github_source import GitHubSource
from ingestion.application.repository_clone.saved_repository_loader import SavedRepositoryLoader
from ingestion.domain.errors.scan_errors import ScanError
from ingestion.infrastructure.azure_devops.azure_devops_api_client import (
    AzureDevOpsApiClient,
    create_azure_devops_http_client,
)
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
    github_scan_handler: GitHubScanHandler
    github_clone_handler: RepositoryCloneHandler
    azure_devops_scan_handler: AzureDevOpsScanHandler
    azure_devops_clone_handler: RepositoryCloneHandler
    saved_repositories: SavedRepositoryLoader
    http_clients: tuple[httpx.AsyncClient, ...]
    runner: BackgroundRunner

    async def aclose(self) -> None:
        await self.runner.aclose()
        for client in self.http_clients:
            await client.aclose()


@dataclass(frozen=True)
class _SharedSaving:
    """One store, job registry, runner and git workspace serve every provider."""

    settings: IngestionSettings
    store: CloneFolderStore
    registry: CloneJobRegistry
    runner: BackgroundRunner
    workspace: GitRepositoryWorkspace

    def handler_for(self, source: RepositorySource) -> RepositoryCloneHandler:
        deps = RepositoryCloneDeps(
            source=source,
            workspace=self.workspace,
            store=self.store,
            registry=self.registry,
            runner=self.runner,
            max_repo_bytes=self.settings.clone_max_repo_bytes,
        )
        return RepositoryCloneHandler(deps)


async def _shared_saving(settings: IngestionSettings) -> _SharedSaving:
    store = build_clone_store(settings)
    try:
        await store.ensure_ready()
    except ScanError as exc:
        # Storage being down must not stop ingestion; save endpoints report it instead.
        _logger.warning("Clone storage unavailable at startup: %s", exc)
    return _SharedSaving(
        settings=settings,
        store=store,
        registry=CloneJobRegistry(),
        runner=BackgroundRunner(),
        workspace=GitRepositoryWorkspace(settings),
    )


async def build_scan_services(settings: IngestionSettings) -> ScanServices:
    github_http = create_github_http_client(settings)
    azure_http = create_azure_devops_http_client(settings)
    github_reader = GitHubApiClient(github_http, settings.github_scan_max_pages)
    azure_reader = AzureDevOpsApiClient(azure_http, settings.azdo_api_version)
    saving = await _shared_saving(settings)
    github_source = GitHubSource(github_reader, settings.github_clone_base_url)
    azure_source = AzureDevOpsSource(azure_reader, settings.azdo_api_base_url)
    sources = {source.provider: source for source in (github_source, azure_source)}
    return ScanServices(
        github_scan_handler=GitHubScanHandler(github_reader),
        github_clone_handler=saving.handler_for(github_source),
        azure_devops_scan_handler=AzureDevOpsScanHandler(azure_reader),
        azure_devops_clone_handler=saving.handler_for(azure_source),
        saved_repositories=SavedRepositoryLoader(saving.store, sources, settings.clone_workdir),
        http_clients=(github_http, azure_http),
        runner=saving.runner,
    )
