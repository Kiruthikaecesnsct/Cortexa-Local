from pathlib import Path
from unittest.mock import AsyncMock

import pytest

from ingestion.application.git.clone_adapter import CloneAdapter
from ingestion.infrastructure.config.settings import IngestionSettings
from ingestion.infrastructure.secrets.keyvault_client import KeyVaultClient

GITHUB_URL = "https://github.com/acme/my-repo.git"
FAKE_TOKEN = "ghp_faketokenABCDEFGHIJKLMNOP123456"
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


async def test_clone_tokenless_public_repo_no_get_secret_call(adapter, mocker):
    mock_get_secret = mocker.patch.object(
        adapter._secret_client, "get_secret", new=AsyncMock(return_value=FAKE_TOKEN)
    )
    mocker.patch("ingestion.application.git.clone_adapter.size_checker.check_size", new=AsyncMock())
    mocker.patch(
        "ingestion.application.git.clone_adapter.git_runner.run_clone",
        new=AsyncMock(return_value=FAKE_CLONE_DEST),
    )

    result = await adapter.clone(GITHUB_URL, None, None)

    assert result == Path(FAKE_CLONE_DEST)
    mock_get_secret.assert_not_awaited()


async def test_clone_tokenless_passes_none_to_size_checker(adapter, mocker):
    mocker.patch.object(adapter._secret_client, "get_secret", new=AsyncMock())
    mock_check_size = mocker.patch(
        "ingestion.application.git.clone_adapter.size_checker.check_size", new=AsyncMock()
    )
    mocker.patch(
        "ingestion.application.git.clone_adapter.git_runner.run_clone",
        new=AsyncMock(return_value=FAKE_CLONE_DEST),
    )

    await adapter.clone(GITHUB_URL, None, None)

    call_args = mock_check_size.await_args
    assert call_args[0][1] is None


async def test_clone_tokenless_passes_none_to_git_runner(adapter, mocker):
    mocker.patch.object(adapter._secret_client, "get_secret", new=AsyncMock())
    mocker.patch("ingestion.application.git.clone_adapter.size_checker.check_size", new=AsyncMock())
    mock_run_clone = mocker.patch(
        "ingestion.application.git.clone_adapter.git_runner.run_clone",
        new=AsyncMock(return_value=FAKE_CLONE_DEST),
    )

    await adapter.clone(GITHUB_URL, None, None)

    call_args = mock_run_clone.await_args
    assert call_args[0][1] is None


async def test_clone_with_secret_name_calls_get_secret(adapter, mocker):
    secret_name = "batch-git-pat-abc123"
    mock_get_secret = mocker.patch.object(
        adapter._secret_client, "get_secret", new=AsyncMock(return_value=FAKE_TOKEN)
    )
    mocker.patch("ingestion.application.git.clone_adapter.size_checker.check_size", new=AsyncMock())
    mocker.patch(
        "ingestion.application.git.clone_adapter.git_runner.run_clone",
        new=AsyncMock(return_value=FAKE_CLONE_DEST),
    )

    await adapter.clone(GITHUB_URL, secret_name, None)

    mock_get_secret.assert_awaited_once_with(secret_name)


async def test_clone_with_secret_name_passes_resolved_token_to_runner(adapter, mocker):
    secret_name = "batch-git-pat-xyz"
    mocker.patch.object(
        adapter._secret_client, "get_secret", new=AsyncMock(return_value=FAKE_TOKEN)
    )
    mocker.patch("ingestion.application.git.clone_adapter.size_checker.check_size", new=AsyncMock())
    mock_run_clone = mocker.patch(
        "ingestion.application.git.clone_adapter.git_runner.run_clone",
        new=AsyncMock(return_value=FAKE_CLONE_DEST),
    )

    await adapter.clone(GITHUB_URL, secret_name, None)

    call_args = mock_run_clone.await_args
    assert call_args[0][1] == FAKE_TOKEN


async def test_clone_with_branch_passes_branch_to_runner(adapter, mocker):
    branch = "feature/my-branch"
    mocker.patch.object(
        adapter._secret_client, "get_secret", new=AsyncMock(return_value=FAKE_TOKEN)
    )
    mocker.patch("ingestion.application.git.clone_adapter.size_checker.check_size", new=AsyncMock())
    mock_run_clone = mocker.patch(
        "ingestion.application.git.clone_adapter.git_runner.run_clone",
        new=AsyncMock(return_value=FAKE_CLONE_DEST),
    )

    await adapter.clone(GITHUB_URL, "batch-git-pat-test", branch)

    call_args = mock_run_clone.await_args
    assert call_args[0][3] == branch


async def test_clone_tokenless_with_branch_passes_both(adapter, mocker):
    branch = "develop"
    mocker.patch.object(adapter._secret_client, "get_secret", new=AsyncMock())
    mocker.patch("ingestion.application.git.clone_adapter.size_checker.check_size", new=AsyncMock())
    mock_run_clone = mocker.patch(
        "ingestion.application.git.clone_adapter.git_runner.run_clone",
        new=AsyncMock(return_value=FAKE_CLONE_DEST),
    )

    await adapter.clone(GITHUB_URL, None, branch)

    call_args = mock_run_clone.await_args
    assert call_args[0][1] is None
    assert call_args[0][3] == branch
