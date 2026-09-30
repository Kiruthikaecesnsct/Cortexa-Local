from ingestion.application.github_scan.github_port import GitHubRepositoryReader
from ingestion.application.github_scan.scan_target import (
    parse_owner,
    validate_branch,
    validate_owner,
    validate_repository,
)
from ingestion.application.repository_clone.clone_ports import SaveRequest
from ingestion.domain.enums.git_host import GitHost
from ingestion.domain.enums.source_provider import SourceProvider
from ingestion.domain.models.repository_clone import CheckoutSpec, SaveTarget

_BYTES_PER_KB = 1024


class GitHubSource:
    """GitHub rules for saving a branch."""

    provider = SourceProvider.GITHUB

    def __init__(self, reader: GitHubRepositoryReader, clone_base_url: str) -> None:
        self._reader = reader
        self._clone_base_url = clone_base_url.rstrip("/")

    def target_from_request(self, request: SaveRequest) -> SaveTarget:
        return self.target_from_names(
            parse_owner(request.org_url), request.repository, request.branch
        )

    def target_from_names(self, owner: str, repository: str, branch: str) -> SaveTarget:
        return SaveTarget(
            provider=self.provider,
            owner=validate_owner(owner),
            repository=validate_repository(repository),
            branch=validate_branch(branch),
        )

    async def repository_size_bytes(self, target: SaveTarget, token: str) -> int:
        summary = await self._reader.get_repository(target.owner, target.repository, token)
        return summary.size_kb * _BYTES_PER_KB

    def checkout_spec(self, target: SaveTarget, token: str) -> CheckoutSpec:
        url = f"{self._clone_base_url}/{target.owner}/{target.repository}.git"
        return CheckoutSpec(host=GitHost.GITHUB, url=url, branch=target.branch, token=token)
