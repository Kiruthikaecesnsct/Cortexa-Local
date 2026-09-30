import pytest

from ingestion.application.github_scan.scan_target import (
    parse_owner,
    validate_branch,
    validate_repository,
)
from ingestion.domain.errors.github_scan_errors import InvalidScanTargetError


@pytest.mark.parametrize(
    "url",
    [
        "https://github.com/acme",
        "https://github.com/acme/",
        "https://www.github.com/acme",
        "https://github.com/orgs/acme",
        "  https://github.com/acme  ",
        "https://github.com/acme?tab=repositories",
    ],
)
def test_parse_owner_accepts_org_urls(url: str) -> None:
    assert parse_owner(url) == "acme"


@pytest.mark.parametrize(
    "url",
    [
        "http://github.com/acme",
        "https://gitlab.com/acme",
        "https://github.com.evil.io/acme",
        "https://evil.io/github.com/acme",
        "https://github.com/",
        "https://github.com/acme/repo",
        "https://github.com/-acme",
        "https://github.com/ac_me",
        "acme",
    ],
)
def test_parse_owner_rejects_other_urls(url: str) -> None:
    with pytest.raises(InvalidScanTargetError):
        parse_owner(url)


@pytest.mark.parametrize("repo", ["api", "my.repo", "my_repo-2"])
def test_validate_repository_accepts_valid_names(repo: str) -> None:
    assert validate_repository(repo) == repo


@pytest.mark.parametrize("repo", ["..", ".", "a/b", "a b", "a?b"])
def test_validate_repository_rejects_invalid_names(repo: str) -> None:
    with pytest.raises(InvalidScanTargetError):
        validate_repository(repo)


@pytest.mark.parametrize("branch", ["main", "feature/scan", "release-1.2"])
def test_validate_branch_accepts_valid_names(branch: str) -> None:
    assert validate_branch(branch) == branch


@pytest.mark.parametrize(
    "branch", ["../main", "/main", "main/", "a//b", "main.lock", "a b", "a?b", "a%2Fb"]
)
def test_validate_branch_rejects_invalid_names(branch: str) -> None:
    with pytest.raises(InvalidScanTargetError):
        validate_branch(branch)
