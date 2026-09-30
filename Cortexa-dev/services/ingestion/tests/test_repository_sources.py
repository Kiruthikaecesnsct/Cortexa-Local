import pytest
from pydantic import SecretStr

from ingestion.application.dtos.azure_devops_scan_dtos import (
    RepositoryTreeRequest as AzureRequest,
)
from ingestion.application.repository_clone.azure_devops_source import AzureDevOpsSource
from ingestion.application.repository_clone.github_source import GitHubSource
from ingestion.domain.enums.git_host import GitHost
from ingestion.domain.enums.source_provider import SourceProvider
from ingestion.domain.errors.azure_devops_scan_errors import (
    InvalidScanTargetError as AzureInvalidTarget,
)
from ingestion.domain.errors.github_scan_errors import (
    InvalidScanTargetError as GitHubInvalidTarget,
)
from ingestion.domain.models.azure_devops_scan import RepositorySummary as AzureSummary
from ingestion.domain.models.github_scan import RepositorySummary as GitHubSummary

TOKEN = "tok"


class GitHubReader:
    async def get_repository(self, owner: str, repo: str, token: str) -> GitHubSummary:
        return GitHubSummary(
            name=repo, full_name="x", private=False, default_branch="main", html_url="x", size_kb=3
        )


class AzureReader:
    def __init__(self) -> None:
        self.calls: list[tuple[str, str, str]] = []

    async def get_repository(
        self, organization: str, project: str, repository: str, token: str
    ) -> AzureSummary:
        self.calls.append((organization, project, repository))
        return AzureSummary(name="x", full_name="x", html_url="x", size_kb=2)


def _azure(base: str = "https://dev.azure.com/") -> AzureDevOpsSource:
    return AzureDevOpsSource(AzureReader(), base)


def test_azure_target_from_request_splits_org_project_and_repo() -> None:
    request = AzureRequest(
        org_url="https://dev.azure.com/contoso",
        pat=SecretStr(TOKEN),
        repository="Platform Team/api",
        branch="feature/x",
    )

    target = _azure().target_from_request(request)

    assert target.provider == SourceProvider.AZURE_DEVOPS
    assert (target.owner, target.repository, target.branch) == (
        "contoso",
        "Platform Team/api",
        "feature/x",
    )
    assert target.repository_name == "api"


def test_azure_clone_url_quotes_each_segment() -> None:
    source = _azure()
    target = source.target_from_names("contoso", "Platform Team/my repo", "main")

    spec = source.checkout_spec(target, TOKEN)

    assert spec.host == GitHost.AZURE_DEVOPS
    assert spec.url == "https://dev.azure.com/contoso/Platform%20Team/_git/my%20repo"
    assert spec.branch == "main"


@pytest.mark.parametrize(
    ("owner", "repository", "branch"),
    [
        ("-bad", "Platform/api", "main"),
        ("contoso", "api", "main"),
        ("contoso", "Platform/api/extra", "main"),
        ("contoso", "../api", "main"),
        ("contoso", "Platform/api", "../../etc"),
    ],
)
def test_azure_rejects_invalid_names(owner: str, repository: str, branch: str) -> None:
    with pytest.raises(AzureInvalidTarget):
        _azure().target_from_names(owner, repository, branch)


async def test_azure_size_uses_project_and_repository() -> None:
    reader = AzureReader()
    source = AzureDevOpsSource(reader, "https://dev.azure.com")

    size = await source.repository_size_bytes(
        source.target_from_names("contoso", "Platform/api", "main"), TOKEN
    )

    assert size == 2 * 1024
    assert reader.calls == [("contoso", "Platform", "api")]


def test_github_clone_url_and_validation() -> None:
    source = GitHubSource(GitHubReader(), "https://github.com/")
    target = source.target_from_names("acme", "api", "main")

    assert source.checkout_spec(target, TOKEN).url == "https://github.com/acme/api.git"
    with pytest.raises(GitHubInvalidTarget):
        source.target_from_names("acme", "Platform/api", "main")


def test_clone_ids_differ_between_providers() -> None:
    github = GitHubSource(GitHubReader(), "https://github.com").target_from_names("a", "b", "c")
    azure = _azure().target_from_names("a", "p/b", "c")

    assert github.clone_id != azure.clone_id
