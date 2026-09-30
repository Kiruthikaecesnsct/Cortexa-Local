import pytest
from pydantic import SecretStr

from ingestion.application.dtos.azure_devops_scan_dtos import (
    ListBranchesRequest,
    ListRepositoriesRequest,
    RepositoryTreeRequest,
)
from ingestion.application.handlers.azure_devops_scan_handler import AzureDevOpsScanHandler
from ingestion.domain.errors.azure_devops_scan_errors import InvalidScanTargetError
from ingestion.domain.models.azure_devops_scan import (
    BranchSummary,
    RepositorySummary,
    RepositoryTree,
    TreeEntry,
)

TOKEN = "azdo_testtoken"
ORG_URL = "https://dev.azure.com/acme"


def _repo(name: str) -> RepositorySummary:
    return RepositorySummary(
        name=f"Project/{name}",
        full_name=f"acme/Project/{name}",
        html_url=f"https://dev.azure.com/acme/Project/_git/{name}",
    )


class FakeReader:
    def __init__(self) -> None:
        self.calls: list[tuple] = []

    async def list_repositories(self, organization: str, token: str) -> list[RepositorySummary]:
        self.calls.append(("list", organization, token))
        return [_repo("zeta"), _repo("Alpha"), _repo("beta")]

    async def list_branches(
        self, organization: str, project: str, repository: str, token: str
    ) -> list[BranchSummary]:
        self.calls.append(("branches", organization, project, repository, token))
        return [BranchSummary(name="main"), BranchSummary(name="Dev"), BranchSummary(name="alpha")]

    async def get_tree(
        self, organization: str, project: str, repository: str, branch: str, token: str
    ) -> RepositoryTree:
        self.calls.append(("tree", organization, project, repository, branch, token))
        return RepositoryTree(
            repository=repository, branch=branch, entries=[TreeEntry(path="README.md", type="blob")]
        )


@pytest.fixture
def reader() -> FakeReader:
    return FakeReader()


async def test_list_repositories_sorts_by_name_and_passes_secret(reader: FakeReader) -> None:
    handler = AzureDevOpsScanHandler(reader)

    result = await handler.list_repositories(
        ListRepositoriesRequest(org_url=ORG_URL, pat=SecretStr(TOKEN))
    )

    assert result.owner == "acme"
    names = [r.name for r in result.repositories]
    assert names == ["Project/Alpha", "Project/beta", "Project/zeta"]
    assert reader.calls == [("list", "acme", TOKEN)]


async def test_list_repositories_rejects_invalid_url_without_calling_azure(
    reader: FakeReader,
) -> None:
    handler = AzureDevOpsScanHandler(reader)

    with pytest.raises(InvalidScanTargetError):
        await handler.list_repositories(
            ListRepositoriesRequest(org_url="https://evil.io/acme", pat=SecretStr(TOKEN))
        )
    assert reader.calls == []


async def test_get_tree_validates_and_delegates(reader: FakeReader) -> None:
    handler = AzureDevOpsScanHandler(reader)

    tree = await handler.get_tree(
        RepositoryTreeRequest(
            org_url=ORG_URL, pat=SecretStr(TOKEN), repository="Project/api", branch="feature/x"
        )
    )

    assert tree.entries[0].path == "README.md"
    assert tree.repository == "Project/api"
    assert reader.calls == [("tree", "acme", "Project", "api", "feature/x", TOKEN)]


async def test_get_tree_rejects_path_traversal_branch(reader: FakeReader) -> None:
    handler = AzureDevOpsScanHandler(reader)

    with pytest.raises(InvalidScanTargetError):
        await handler.get_tree(
            RepositoryTreeRequest(
                org_url=ORG_URL, pat=SecretStr(TOKEN), repository="Project/api", branch="../../user"
            )
        )
    assert reader.calls == []


def test_request_repr_does_not_expose_token() -> None:
    request = ListRepositoriesRequest(org_url=ORG_URL, pat=SecretStr(TOKEN))

    assert TOKEN not in repr(request)
    assert TOKEN not in str(request.model_dump())


async def test_list_branches_sorts_by_name(reader: FakeReader) -> None:
    handler = AzureDevOpsScanHandler(reader)

    result = await handler.list_branches(
        ListBranchesRequest(org_url=ORG_URL, pat=SecretStr(TOKEN), repository="Project/api")
    )

    assert result.repository == "Project/api"
    assert [b.name for b in result.branches] == ["alpha", "Dev", "main"]
    assert reader.calls == [("branches", "acme", "Project", "api", TOKEN)]


async def test_list_branches_rejects_invalid_repository(reader: FakeReader) -> None:
    handler = AzureDevOpsScanHandler(reader)

    with pytest.raises(InvalidScanTargetError):
        await handler.list_branches(
            ListBranchesRequest(org_url=ORG_URL, pat=SecretStr(TOKEN), repository="../x")
        )
    assert reader.calls == []
