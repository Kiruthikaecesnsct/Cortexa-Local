import httpx
import pytest
import respx

from ingestion.application.git import size_checker
from ingestion.domain.enums.git_host import GitHost
from ingestion.domain.models.clone_request import RepoRef
from ingestion.infrastructure.config.settings import IngestionSettings

SIZE_100_MB_BYTES = 100 * 1024 * 1024
LIMIT_500_MB_BYTES = 500 * 1024 * 1024


@pytest.fixture
def settings() -> IngestionSettings:
    return IngestionSettings(clone_max_repo_bytes=LIMIT_500_MB_BYTES)


@pytest.fixture
def github_ref() -> RepoRef:
    return RepoRef(
        host=GitHost.GITHUB,
        owner="acme",
        repo="my-repo",
        normalized_https_url="https://github.com/acme/my-repo.git",
    )


@pytest.fixture
def azdo_ref() -> RepoRef:
    return RepoRef(
        host=GitHost.AZURE_DEVOPS,
        owner="myorg",
        repo="myproject/myrepo",
        normalized_https_url="https://dev.azure.com/myorg/myproject/_git/myrepo",
    )


@respx.mock
async def test_github_tokenless_does_not_send_authorization_header(github_ref, settings):
    size_kb = SIZE_100_MB_BYTES // 1024
    route = respx.get("https://api.github.com/repos/acme/my-repo").mock(
        return_value=httpx.Response(200, json={"size": size_kb})
    )

    await size_checker.check_size(github_ref, None, settings)

    assert route.called
    request = route.calls[0].request
    assert "Authorization" not in request.headers


@respx.mock
async def test_github_with_token_sends_authorization_header(github_ref, settings):
    token = "ghp_testtoken123"
    size_kb = SIZE_100_MB_BYTES // 1024
    route = respx.get("https://api.github.com/repos/acme/my-repo").mock(
        return_value=httpx.Response(200, json={"size": size_kb})
    )

    await size_checker.check_size(github_ref, token, settings)

    assert route.called
    request = route.calls[0].request
    assert "Authorization" in request.headers
    assert request.headers["Authorization"] == f"token {token}"


@respx.mock
async def test_azdo_tokenless_does_not_send_authorization_header(azdo_ref, settings):
    route = respx.get(
        "https://dev.azure.com/myorg/myproject/_apis/git/repositories/myrepo?api-version=7.1"
    ).mock(return_value=httpx.Response(200, json={"size": SIZE_100_MB_BYTES}))

    await size_checker.check_size(azdo_ref, None, settings)

    assert route.called
    request = route.calls[0].request
    assert "Authorization" not in request.headers


@respx.mock
async def test_azdo_with_token_sends_authorization_header(azdo_ref, settings):
    token = "azdo_testpat"
    route = respx.get(
        "https://dev.azure.com/myorg/myproject/_apis/git/repositories/myrepo?api-version=7.1"
    ).mock(return_value=httpx.Response(200, json={"size": SIZE_100_MB_BYTES}))

    await size_checker.check_size(azdo_ref, token, settings)

    assert route.called
    request = route.calls[0].request
    assert "Authorization" in request.headers
    assert "Basic" in request.headers["Authorization"]
