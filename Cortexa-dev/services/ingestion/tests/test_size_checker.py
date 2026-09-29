import httpx
import pytest
import respx

from ingestion.application.git import size_checker
from ingestion.domain.enums.git_host import GitHost
from ingestion.domain.errors.clone_errors import RepoTooLargeError
from ingestion.domain.models.clone_request import RepoRef
from ingestion.infrastructure.config.settings import IngestionSettings

GITHUB_TOKEN = "ghp_testtoken"
AZDO_TOKEN = "azdo_testtoken"

SIZE_100_MB_BYTES = 100 * 1024 * 1024
SIZE_600_MB_BYTES = 600 * 1024 * 1024
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
async def test_github_under_size_cap_does_not_raise(github_ref, settings):
    size_kb = SIZE_100_MB_BYTES // 1024
    respx.get("https://api.github.com/repos/acme/my-repo").mock(
        return_value=httpx.Response(200, json={"size": size_kb})
    )

    await size_checker.check_size(github_ref, GITHUB_TOKEN, settings)


@respx.mock
async def test_github_over_size_cap_raises_repo_too_large(github_ref, settings):
    size_kb = SIZE_600_MB_BYTES // 1024
    respx.get("https://api.github.com/repos/acme/my-repo").mock(
        return_value=httpx.Response(200, json={"size": size_kb})
    )

    with pytest.raises(RepoTooLargeError) as exc_info:
        await size_checker.check_size(github_ref, GITHUB_TOKEN, settings)

    assert exc_info.value.size_bytes > LIMIT_500_MB_BYTES
    assert exc_info.value.limit_bytes == LIMIT_500_MB_BYTES


@respx.mock
async def test_azdo_under_size_cap_does_not_raise(azdo_ref, settings):
    respx.get(
        "https://dev.azure.com/myorg/myproject/_apis/git/repositories/myrepo?api-version=7.1"
    ).mock(return_value=httpx.Response(200, json={"size": SIZE_100_MB_BYTES}))

    await size_checker.check_size(azdo_ref, AZDO_TOKEN, settings)


@respx.mock
async def test_azdo_over_size_cap_raises_repo_too_large(azdo_ref, settings):
    respx.get(
        "https://dev.azure.com/myorg/myproject/_apis/git/repositories/myrepo?api-version=7.1"
    ).mock(return_value=httpx.Response(200, json={"size": SIZE_600_MB_BYTES}))

    with pytest.raises(RepoTooLargeError) as exc_info:
        await size_checker.check_size(azdo_ref, AZDO_TOKEN, settings)

    assert exc_info.value.size_bytes == SIZE_600_MB_BYTES


@respx.mock
async def test_github_api_404_raises_http_status_error(github_ref, settings):
    respx.get("https://api.github.com/repos/acme/my-repo").mock(return_value=httpx.Response(404))

    with pytest.raises(httpx.HTTPStatusError):
        await size_checker.check_size(github_ref, GITHUB_TOKEN, settings)


@respx.mock
async def test_github_network_error_raises_connect_error(github_ref, settings):
    respx.get("https://api.github.com/repos/acme/my-repo").mock(
        side_effect=httpx.ConnectError("network down")
    )

    with pytest.raises(httpx.ConnectError):
        await size_checker.check_size(github_ref, GITHUB_TOKEN, settings)
