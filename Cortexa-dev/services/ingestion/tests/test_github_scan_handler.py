import pytest
from pydantic import SecretStr

from ingestion.application.dtos.github_scan_dtos import (
    ListBranchesRequest,
    ListRepositoriesRequest,
    RepositoryTreeRequest,
)
from ingestion.application.handlers.github_scan_handler import GitHubScanHandler
from ingestion.domain.errors.github_scan_errors import InvalidScanTargetError
from ingestion.domain.models.github_scan import (
    BranchSummary,
    RepositorySummary,
    RepositoryTree,
    TreeEntry,
)

TOKEN = "ghp_testtoken"
ORG_URL = "https://github.com/acme"


def _repo(name: str) -> RepositorySummary:
    return RepositorySummary(
        name=name,
        full_name=f"acme/{name}",
        private=False,
        default_branch="main",
        html_url=f"https://github.com/acme/{name}",
    )


class FakeReader:
    def __init__(self) -> None:
        self.calls: list[tuple] = []

    async def list_repositories(self, owner: str, token: str) -> list[RepositorySummary]:
        self.calls.append(("list", owner, token))
        return [_repo("zeta"), _repo("Alpha"), _repo("beta")]

    async def list_branches(self, owner: str, repo: str, token: str) -> list[BranchSummary]:
        self.calls.append(("branches", owner, repo, token))
        return [BranchSummary(name="main"), BranchSummary(name="Dev"), BranchSummary(name="alpha")]

    async def get_tree(self, owner: str, repo: str, branch: str, token: str) -> RepositoryTree:
        self.calls.append(("tree", owner, repo, branch, token))
        return RepositoryTree(
            repository=repo, branch=branch, entries=[TreeEntry(path="README.md", type="blob")]
        )


@pytest.fixture
def reader() -> FakeReader:
    return FakeReader()


async def test_list_repositories_sorts_by_name_and_passes_secret(reader: FakeReader) -> None:
    handler = GitHubScanHandler(reader)

    result = await handler.list_repositories(
        ListRepositoriesRequest(org_url=ORG_URL, pat=SecretStr(TOKEN))
    )

    assert result.owner == "acme"
    assert [r.name for r in result.repositories] == ["Alpha", "beta", "zeta"]
    assert reader.calls == [("list", "acme", TOKEN)]


async def test_list_repositories_rejects_invalid_url_without_calling_github(
    reader: FakeReader,
) -> None:
    handler = GitHubScanHandler(reader)

    with pytest.raises(InvalidScanTargetError):
        await handler.list_repositories(
            ListRepositoriesRequest(org_url="https://evil.io/acme", pat=SecretStr(TOKEN))
        )
    assert reader.calls == []


async def test_get_tree_validates_and_delegates(reader: FakeReader) -> None:
    handler = GitHubScanHandler(reader)

    tree = await handler.get_tree(
        RepositoryTreeRequest(
            org_url=ORG_URL, pat=SecretStr(TOKEN), repository="api", branch="feature/x"
        )
    )

    assert tree.entries[0].path == "README.md"
    assert reader.calls == [("tree", "acme", "api", "feature/x", TOKEN)]


async def test_get_tree_rejects_path_traversal_branch(reader: FakeReader) -> None:
    handler = GitHubScanHandler(reader)

    with pytest.raises(InvalidScanTargetError):
        await handler.get_tree(
            RepositoryTreeRequest(
                org_url=ORG_URL, pat=SecretStr(TOKEN), repository="api", branch="../../user"
            )
        )
    assert reader.calls == []


def test_request_repr_does_not_expose_token() -> None:
    request = ListRepositoriesRequest(org_url=ORG_URL, pat=SecretStr(TOKEN))

    assert TOKEN not in repr(request)
    assert TOKEN not in str(request.model_dump())


async def test_list_branches_sorts_by_name(reader: FakeReader) -> None:
    handler = GitHubScanHandler(reader)

    result = await handler.list_branches(
        ListBranchesRequest(org_url=ORG_URL, pat=SecretStr(TOKEN), repository="api")
    )

    assert result.repository == "api"
    assert [b.name for b in result.branches] == ["alpha", "Dev", "main"]
    assert reader.calls == [("branches", "acme", "api", TOKEN)]


async def test_list_branches_rejects_invalid_repository(reader: FakeReader) -> None:
    handler = GitHubScanHandler(reader)

    with pytest.raises(InvalidScanTargetError):
        await handler.list_branches(
            ListBranchesRequest(org_url=ORG_URL, pat=SecretStr(TOKEN), repository="../x")
        )
    assert reader.calls == []
