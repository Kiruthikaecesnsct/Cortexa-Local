import pytest

from ingestion.application.azure_devops_scan.scan_target import (
    parse_organization,
    split_repository,
    validate_branch,
    validate_repository,
)
from ingestion.domain.errors.azure_devops_scan_errors import InvalidScanTargetError


@pytest.mark.parametrize(
    "url",
    [
        "https://dev.azure.com/acme",
        "https://dev.azure.com/acme/",
        "  https://dev.azure.com/acme  ",
        "https://dev.azure.com/acme?tab=repos",
    ],
)
def test_parse_organization_accepts_org_urls(url: str) -> None:
    assert parse_organization(url) == "acme"


@pytest.mark.parametrize(
    "url",
    [
        "http://dev.azure.com/acme",
        "https://gitlab.com/acme",
        "https://dev.azure.com.evil.io/acme",
        "https://evil.io/dev.azure.com/acme",
        "https://dev.azure.com/",
        "https://dev.azure.com/acme/project",
        "https://acme.visualstudio.com/",
        "acme",
    ],
)
def test_parse_organization_rejects_other_urls(url: str) -> None:
    with pytest.raises(InvalidScanTargetError):
        parse_organization(url)


@pytest.mark.parametrize("repo", ["Project/api", "my project/my.repo", "P1/repo_2-x"])
def test_validate_repository_accepts_valid_names(repo: str) -> None:
    assert validate_repository(repo) == repo


@pytest.mark.parametrize("repo", ["api", "a/b/c", "../x", "a//b", "./api", "P1/.."])
def test_validate_repository_rejects_invalid_names(repo: str) -> None:
    with pytest.raises(InvalidScanTargetError):
        validate_repository(repo)


def test_split_repository_returns_project_and_repo() -> None:
    assert split_repository("Project One/my-repo") == ("Project One", "my-repo")


@pytest.mark.parametrize("branch", ["main", "feature/scan", "release-1.2"])
def test_validate_branch_accepts_valid_names(branch: str) -> None:
    assert validate_branch(branch) == branch


@pytest.mark.parametrize("branch", ["../main", "/main", "main/", "a//b", "main.lock", "a b", "a?b"])
def test_validate_branch_rejects_invalid_names(branch: str) -> None:
    with pytest.raises(InvalidScanTargetError):
        validate_branch(branch)
