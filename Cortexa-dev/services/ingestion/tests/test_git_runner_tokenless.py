from unittest.mock import AsyncMock, patch

import pytest

from ingestion.domain.enums.git_host import GitHost
from ingestion.domain.models.clone_request import RepoRef
from ingestion.infrastructure.config.settings import IngestionSettings
from ingestion.infrastructure.git import git_runner

FAKE_DEST = "/tmp/cortexa-clones/abc123"


@pytest.fixture
def settings(tmp_path) -> IngestionSettings:
    return IngestionSettings(
        clone_workdir=str(tmp_path),
        clone_timeout_seconds=5.0,
    )


@pytest.fixture
def github_ref() -> RepoRef:
    return RepoRef(
        host=GitHost.GITHUB,
        owner="acme",
        repo="my-repo",
        normalized_https_url="https://github.com/acme/my-repo.git",
    )


def _make_mock_process(returncode: int = 0, stderr_bytes: bytes = b""):
    from unittest.mock import MagicMock

    proc = MagicMock()
    proc.pid = 12345
    proc.returncode = returncode
    proc.wait = AsyncMock(return_value=None)
    proc.stderr = AsyncMock()
    proc.stderr.read = AsyncMock(return_value=stderr_bytes)
    return proc


async def test_run_clone_tokenless_does_not_set_git_config_env(github_ref, settings):
    proc = _make_mock_process(returncode=0)

    with patch("asyncio.create_subprocess_exec", new=AsyncMock(return_value=proc)) as mock_exec:
        with patch("tempfile.mkdtemp", return_value=FAKE_DEST):
            await git_runner.run_clone(github_ref, None, settings)

    call_kwargs = mock_exec.await_args.kwargs
    env = call_kwargs["env"]
    assert "GIT_CONFIG_COUNT" not in env
    assert "GIT_CONFIG_KEY_0" not in env
    assert "GIT_CONFIG_VALUE_0" not in env


async def test_run_clone_with_token_sets_git_config_env(github_ref, settings):
    token = "ghp_testtoken123"
    proc = _make_mock_process(returncode=0)

    with patch("asyncio.create_subprocess_exec", new=AsyncMock(return_value=proc)) as mock_exec:
        with patch("tempfile.mkdtemp", return_value=FAKE_DEST):
            await git_runner.run_clone(github_ref, token, settings)

    call_kwargs = mock_exec.await_args.kwargs
    env = call_kwargs["env"]
    assert env["GIT_CONFIG_COUNT"] == "1"
    assert env["GIT_CONFIG_KEY_0"] == "http.extraHeader"
    assert "Authorization: Basic" in env["GIT_CONFIG_VALUE_0"]


async def test_run_clone_with_branch_passes_branch_arg(github_ref, settings):
    branch = "feature/test-branch"
    proc = _make_mock_process(returncode=0)

    with patch("asyncio.create_subprocess_exec", new=AsyncMock(return_value=proc)) as mock_exec:
        with patch("tempfile.mkdtemp", return_value=FAKE_DEST):
            await git_runner.run_clone(github_ref, None, settings, branch)

    call_args = mock_exec.await_args.args
    assert "--branch" in call_args
    assert branch in call_args


async def test_run_clone_without_branch_does_not_pass_branch_arg(github_ref, settings):
    proc = _make_mock_process(returncode=0)

    with patch("asyncio.create_subprocess_exec", new=AsyncMock(return_value=proc)) as mock_exec:
        with patch("tempfile.mkdtemp", return_value=FAKE_DEST):
            await git_runner.run_clone(github_ref, None, settings, None)

    call_args = mock_exec.await_args.args
    assert "--branch" not in call_args


async def test_build_clone_args_without_branch():
    args = git_runner._build_clone_args("/dest", "https://github.com/foo/bar.git", None)

    assert "--branch" not in args
    assert "https://github.com/foo/bar.git" in args
    assert "/dest" in args


async def test_build_clone_args_with_branch():
    args = git_runner._build_clone_args("/dest", "https://github.com/foo/bar.git", "develop")

    assert "--branch" in args
    assert "develop" in args


async def test_build_clone_env_tokenless_returns_env_copy():
    env = git_runner._build_clone_env(None)

    assert "GIT_CONFIG_COUNT" not in env
    assert "GIT_CONFIG_KEY_0" not in env


async def test_build_clone_env_with_token_returns_env_with_config():
    token = "ghp_abc123"

    env = git_runner._build_clone_env(token)

    assert env["GIT_CONFIG_COUNT"] == "1"
    assert env["GIT_CONFIG_KEY_0"] == "http.extraHeader"
    assert "Basic" in env["GIT_CONFIG_VALUE_0"]
