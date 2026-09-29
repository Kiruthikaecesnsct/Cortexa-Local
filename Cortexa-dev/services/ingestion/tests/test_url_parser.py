import pytest

from ingestion.application.git import url_parser
from ingestion.domain.enums.git_host import GitHost
from ingestion.domain.errors.clone_errors import InvalidRepoUrlError, UnsupportedHostError

GITHUB_HTTPS_URL = "https://github.com/acme/my-repo"
GITHUB_HTTPS_URL_GIT = "https://github.com/acme/my-repo.git"
GITHUB_SSH_URL = "git@github.com:acme/my-repo.git"
AZDO_HTTPS_URL = "https://dev.azure.com/myorg/myproject/_git/myrepo"
AZDO_SSH_URL = "git@ssh.dev.azure.com:v3/myorg/myproject/myrepo"
GITLAB_URL = "https://gitlab.com/acme/my-repo.git"
RANDOM_URL = "https://bitbucket.org/acme/my-repo.git"


def test_parse_github_https_without_git_suffix_returns_github_host():
    ref = url_parser.parse(GITHUB_HTTPS_URL)

    assert ref.host == GitHost.GITHUB
    assert ref.owner == "acme"
    assert ref.repo == "my-repo"
    assert ref.use_ssh is False


def test_parse_github_https_with_git_suffix_returns_github_host():
    ref = url_parser.parse(GITHUB_HTTPS_URL_GIT)

    assert ref.host == GitHost.GITHUB
    assert ref.owner == "acme"
    assert ref.repo == "my-repo"
    assert ref.use_ssh is False


def test_parse_github_ssh_returns_use_ssh_true():
    ref = url_parser.parse(GITHUB_SSH_URL)

    assert ref.host == GitHost.GITHUB
    assert ref.use_ssh is True
    assert ref.owner == "acme"
    assert ref.repo == "my-repo"


def test_parse_azdo_https_returns_azure_devops_host():
    ref = url_parser.parse(AZDO_HTTPS_URL)

    assert ref.host == GitHost.AZURE_DEVOPS
    assert ref.owner == "myorg"
    assert ref.use_ssh is False


def test_parse_azdo_ssh_returns_use_ssh_true():
    ref = url_parser.parse(AZDO_SSH_URL)

    assert ref.host == GitHost.AZURE_DEVOPS
    assert ref.use_ssh is True
    assert ref.owner == "myorg"


def test_parse_unknown_host_raises_unsupported_host_error():
    with pytest.raises(UnsupportedHostError):
        url_parser.parse(RANDOM_URL)


def test_parse_gitlab_url_raises_unsupported_host_error():
    with pytest.raises(UnsupportedHostError):
        url_parser.parse(GITLAB_URL)


def test_parse_known_host_with_unmatched_pattern_raises_invalid_repo_url_error():
    malformed = "https://github.com/"

    with pytest.raises(InvalidRepoUrlError):
        url_parser.parse(malformed)


def test_parse_github_https_with_credentials_in_url_raises_invalid_repo_url_error():
    url_with_creds = "https://user:token@github.com/acme/my-repo.git"

    with pytest.raises((InvalidRepoUrlError, UnsupportedHostError)):
        url_parser.parse(url_with_creds)
