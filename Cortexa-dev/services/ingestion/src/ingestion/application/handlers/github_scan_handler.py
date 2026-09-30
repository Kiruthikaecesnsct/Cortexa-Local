import logging

from ingestion.application.dtos.github_scan_dtos import (
    ListBranchesRequest,
    ListBranchesResponse,
    ListRepositoriesRequest,
    ListRepositoriesResponse,
    RepositoryTreeRequest,
)
from ingestion.application.github_scan.github_port import GitHubRepositoryReader
from ingestion.application.github_scan.scan_target import (
    parse_owner,
    validate_branch,
    validate_repository,
)
from ingestion.domain.models.github_scan import RepositoryTree

_logger = logging.getLogger(__name__)


class GitHubScanHandler:
    def __init__(self, reader: GitHubRepositoryReader) -> None:
        self._reader = reader

    async def list_repositories(self, request: ListRepositoriesRequest) -> ListRepositoriesResponse:
        owner = parse_owner(request.org_url)
        repos = await self._reader.list_repositories(owner, request.pat.get_secret_value())
        repos.sort(key=lambda r: r.name.lower())
        _logger.info("GitHub scan listed %d repositories for %s", len(repos), owner)
        return ListRepositoriesResponse(owner=owner, repositories=repos)

    async def list_branches(self, request: ListBranchesRequest) -> ListBranchesResponse:
        owner = parse_owner(request.org_url)
        repo = validate_repository(request.repository)
        branches = await self._reader.list_branches(owner, repo, request.pat.get_secret_value())
        branches.sort(key=lambda b: b.name.lower())
        _logger.info("GitHub scan listed %d branches for %s/%s", len(branches), owner, repo)
        return ListBranchesResponse(repository=repo, branches=branches)

    async def get_tree(self, request: RepositoryTreeRequest) -> RepositoryTree:
        owner = parse_owner(request.org_url)
        repo = validate_repository(request.repository)
        branch = validate_branch(request.branch)
        tree = await self._reader.get_tree(owner, repo, branch, request.pat.get_secret_value())
        _logger.info("GitHub scan read %d entries from %s/%s", len(tree.entries), owner, repo)
        return tree
