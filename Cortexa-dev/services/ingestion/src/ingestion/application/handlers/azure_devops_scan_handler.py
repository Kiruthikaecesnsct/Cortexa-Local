import logging

from ingestion.application.azure_devops_scan.azure_devops_port import AzureDevOpsRepositoryReader
from ingestion.application.azure_devops_scan.scan_target import (
    parse_organization,
    split_repository,
    validate_branch,
)
from ingestion.application.dtos.azure_devops_scan_dtos import (
    ListBranchesRequest,
    ListBranchesResponse,
    ListRepositoriesRequest,
    ListRepositoriesResponse,
    RepositoryTreeRequest,
)
from ingestion.domain.models.azure_devops_scan import RepositoryTree

_logger = logging.getLogger(__name__)


class AzureDevOpsScanHandler:
    def __init__(self, reader: AzureDevOpsRepositoryReader) -> None:
        self._reader = reader

    async def list_repositories(self, request: ListRepositoriesRequest) -> ListRepositoriesResponse:
        organization = parse_organization(request.org_url)
        repos = await self._reader.list_repositories(organization, request.pat.get_secret_value())
        repos.sort(key=lambda r: r.name.lower())
        _logger.info(
            "Azure DevOps scan listed %d repositories for %s", len(repos), organization
        )
        return ListRepositoriesResponse(owner=organization, repositories=repos)

    async def list_branches(self, request: ListBranchesRequest) -> ListBranchesResponse:
        organization = parse_organization(request.org_url)
        project, repo = split_repository(request.repository)
        branches = await self._reader.list_branches(
            organization, project, repo, request.pat.get_secret_value()
        )
        branches.sort(key=lambda b: b.name.lower())
        _logger.info(
            "Azure DevOps scan listed %d branches for %s", len(branches), request.repository
        )
        return ListBranchesResponse(repository=request.repository, branches=branches)

    async def get_tree(self, request: RepositoryTreeRequest) -> RepositoryTree:
        organization = parse_organization(request.org_url)
        project, repo = split_repository(request.repository)
        branch = validate_branch(request.branch)
        tree = await self._reader.get_tree(
            organization, project, repo, branch, request.pat.get_secret_value()
        )
        _logger.info(
            "Azure DevOps scan read %d entries from %s", len(tree.entries), request.repository
        )
        return RepositoryTree(
            repository=request.repository,
            branch=tree.branch,
            entries=tree.entries,
            truncated=tree.truncated,
        )
