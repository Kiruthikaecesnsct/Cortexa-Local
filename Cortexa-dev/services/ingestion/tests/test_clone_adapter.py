import asyncio
from pathlib import Path
from unittest.mock import AsyncMock

import pytest

from ingestion.application.git.clone_adapter import CloneAdapter
from ingestion.domain.errors.clone_errors import (
    CloneError,
    CloneTimeoutError,
    RepoTooLargeError,
)
from ingestion.infrastructure.config.settings import IngestionSettings
from ingestion.infrastructure.secrets.keyvault_client import KeyVaultClient

GITHUB_URL = "https://github.com/acme/my-repo.git"
AZDO_URL = "https://dev.azure.com/myorg/myproject/_git/myrepo"
FAKE_GITHUB_TOKEN = "ghp_faketokenABCDEFGHIJKLMNOP123456"
FAKE_AZDO_TOKEN = "azdo_fakepat_XYZ789"
FAKE_CLONE_DEST = "/tmp/cortexa-clones/fake123"


@pytest.fixture
def settings() -> IngestionSettings:
    return IngestionSettings(clone_max_concurrency=2)


@pytest.fixture
def secret_client(settings) -> KeyVaultClient:
    return KeyVaultClient(settings)


@pytest.fixture
def adapter(settings, secret_client) -> CloneAdapter:
    return CloneAdapter(settings, secret_client)


async def test_clone_github_happy_path_returns_path(adapter, mocker):
    mocker.patch.object(
        adapter._secret_client, "get_secret", new=AsyncMock(return_value=FAKE_GITHUB_TOKEN)
    )
    mocker.patch("ingestion.application.git.clone_adapter.size_checker.check_size", new=AsyncMock())
    mocker.patch(
        "ingestion.application.git.clone_adapter.git_runner.run_clone",
        new=AsyncMock(return_value=FAKE_CLONE_DEST),
    )

    result = await adapter.clone(GITHUB_URL, "batch-git-pat-abc123", None)

    assert result == Path(FAKE_CLONE_DEST)


async def test_clone_azdo_happy_path_returns_path(adapter, mocker):
    mocker.patch.object(
        adapter._secret_client, "get_secret", new=AsyncMock(return_value=FAKE_AZDO_TOKEN)
    )
    mocker.patch("ingestion.application.git.clone_adapter.size_checker.check_size", new=AsyncMock())
    mocker.patch(
        "ingestion.application.git.clone_adapter.git_runner.run_clone",
        new=AsyncMock(return_value=FAKE_CLONE_DEST),
    )

    result = await adapter.clone(AZDO_URL, "batch-git-pat-xyz789", None)

    assert result == Path(FAKE_CLONE_DEST)


async def test_clone_invalid_url_raises_before_token_lookup(adapter, mocker):
    mock_get_secret = mocker.patch.object(
        adapter._secret_client, "get_secret", new=AsyncMock(return_value=FAKE_GITHUB_TOKEN)
    )

    with pytest.raises(CloneError):
        await adapter.clone("https://notavalidurl.example.com/foo", "batch-git-pat-test", None)

    mock_get_secret.assert_not_awaited()


async def test_clone_repo_too_large_raises_before_runner(adapter, mocker):
    mocker.patch.object(
        adapter._secret_client, "get_secret", new=AsyncMock(return_value=FAKE_GITHUB_TOKEN)
    )
    mocker.patch(
        "ingestion.application.git.clone_adapter.size_checker.check_size",
        new=AsyncMock(side_effect=RepoTooLargeError(600 * 1024 * 1024, 500 * 1024 * 1024)),
    )
    mock_runner = mocker.patch(
        "ingestion.application.git.clone_adapter.git_runner.run_clone",
        new=AsyncMock(return_value=FAKE_CLONE_DEST),
    )

    with pytest.raises(RepoTooLargeError):
        await adapter.clone(GITHUB_URL, "batch-git-pat-test", None)

    mock_runner.assert_not_awaited()


async def test_clone_timeout_propagates_from_runner(adapter, mocker):
    mocker.patch.object(
        adapter._secret_client, "get_secret", new=AsyncMock(return_value=FAKE_GITHUB_TOKEN)
    )
    mocker.patch("ingestion.application.git.clone_adapter.size_checker.check_size", new=AsyncMock())
    mocker.patch(
        "ingestion.application.git.clone_adapter.git_runner.run_clone",
        new=AsyncMock(side_effect=CloneTimeoutError(120.0)),
    )

    with pytest.raises(CloneTimeoutError):
        await adapter.clone(GITHUB_URL, "batch-git-pat-test", None)


async def test_clone_semaphore_limits_concurrency(settings, secret_client, mocker):
    max_concurrency = 2
    settings_limited = IngestionSettings(clone_max_concurrency=max_concurrency)
    adapter_limited = CloneAdapter(settings_limited, secret_client)

    active_count = 0
    max_seen = 0
    gate = asyncio.Event()

    async def slow_clone(_ref, _token, _settings, _branch):
        nonlocal active_count, max_seen
        active_count += 1
        max_seen = max(max_seen, active_count)
        await gate.wait()
        active_count -= 1
        return FAKE_CLONE_DEST

    mocker.patch.object(
        adapter_limited._secret_client, "get_secret", new=AsyncMock(return_value=FAKE_GITHUB_TOKEN)
    )
    mocker.patch("ingestion.application.git.clone_adapter.size_checker.check_size", new=AsyncMock())
    mocker.patch("ingestion.application.git.clone_adapter.git_runner.run_clone", new=slow_clone)

    tasks = [
        asyncio.create_task(adapter_limited.clone(GITHUB_URL, "batch-git-pat-test", None))
        for _ in range(5)
    ]
    await asyncio.sleep(0.05)
    gate.set()
    await asyncio.gather(*tasks)

    assert max_seen <= max_concurrency


async def test_clone_token_never_appears_in_raised_exception(adapter, mocker):
    mocker.patch.object(
        adapter._secret_client, "get_secret", new=AsyncMock(return_value=FAKE_GITHUB_TOKEN)
    )
    mocker.patch("ingestion.application.git.clone_adapter.size_checker.check_size", new=AsyncMock())
    mocker.patch(
        "ingestion.application.git.clone_adapter.git_runner.run_clone",
        new=AsyncMock(side_effect=CloneTimeoutError(120.0)),
    )

    with pytest.raises(CloneTimeoutError) as exc_info:
        await adapter.clone(GITHUB_URL, "batch-git-pat-test", None)

    assert FAKE_GITHUB_TOKEN not in str(exc_info.value)
