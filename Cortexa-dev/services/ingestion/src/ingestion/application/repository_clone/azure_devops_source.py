from urllib.parse import quote

from ingestion.application.azure_devops_scan.azure_devops_port import AzureDevOpsRepositoryReader
from ingestion.application.azure_devops_scan.scan_target import (
    parse_organization,
    split_repository,
    validate_branch,
    validate_organization,
    validate_repository,
)
from ingestion.application.repository_clone.clone_ports import SaveRequest
from ingestion.domain.enums.git_host import GitHost
from ingestion.domain.enums.source_provider import SourceProvider
from ingestion.domain.models.repository_clone import CheckoutSpec, SaveTarget

_BYTES_PER_KB = 1024


class AzureDevOpsSource:
    """Azure DevOps rules for saving a branch; repositories are "{project}/{repository}"."""

    provider = SourceProvider.AZURE_DEVOPS

    def __init__(self, reader: AzureDevOpsRepositoryReader, clone_base_url: str) -> None:
        self._reader = reader
        self._clone_base_url = clone_base_url.rstrip("/")

    def target_from_request(self, request: SaveRequest) -> SaveTarget:
        organization = parse_organization(request.org_url)
        return self.target_from_names(organization, request.repository, request.branch)

    def target_from_names(self, owner: str, repository: str, branch: str) -> SaveTarget:
        return SaveTarget(
            provider=self.provider,
            owner=validate_organization(owner),
            repository=validate_repository(repository),
            branch=validate_branch(branch),
        )

    async def repository_size_bytes(self, target: SaveTarget, token: str) -> int:
        project, repo = split_repository(target.repository)
        summary = await self._reader.get_repository(target.owner, project, repo, token)
        return summary.size_kb * _BYTES_PER_KB

    def checkout_spec(self, target: SaveTarget, token: str) -> CheckoutSpec:
        project, repo = split_repository(target.repository)
        # Project and repository names may contain spaces; quote each path segment.
        url = f"{self._clone_base_url}/{quote(target.owner)}/{quote(project)}/_git/{quote(repo)}"
        return CheckoutSpec(host=GitHost.AZURE_DEVOPS, url=url, branch=target.branch, token=token)
