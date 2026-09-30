from typing import Protocol

from ingestion.domain.models.azure_devops_scan import (
    BranchSummary,
    RepositorySummary,
    RepositoryTree,
)


class AzureDevOpsRepositoryReader(Protocol):
    async def list_repositories(self, organization: str, token: str) -> list[RepositorySummary]: ...

    async def list_branches(
        self, organization: str, project: str, repository: str, token: str
    ) -> list[BranchSummary]: ...

    async def get_tree(
        self, organization: str, project: str, repository: str, branch: str, token: str
    ) -> RepositoryTree: ...
