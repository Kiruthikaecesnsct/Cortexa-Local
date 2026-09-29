import asyncio
import signal
from unittest.mock import AsyncMock, MagicMock, patch

import pytest

from ingestion.domain.enums.git_host import GitHost
from ingestion.domain.errors.clone_errors import CloneTimeoutError, GitExecutionError
from ingestion.domain.models.clone_request import RepoRef
from ingestion.infrastructure.config.settings import IngestionSettings
from ingestion.infrastructure.git import git_runner

FAKE_TOKEN = "ghp_supersecrettoken123456789"
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


def _make_mock_process(returncode: int = 0, stderr_bytes: bytes = b"") -> MagicMock:
    proc = MagicMock()
    proc.pid = 12345
    proc.returncode = returncode
    proc.wait = AsyncMock(return_value=None)
    proc.stderr = AsyncMock()
    proc.stderr.read = AsyncMock(return_value=stderr_bytes)
    return proc


async def test_run_clone_success_returns_dest_path(github_ref, settings):
    proc = _make_mock_process(returncode=0)

    with patch("asyncio.create_subprocess_exec", new=AsyncMock(return_value=proc)) as mock_exec:
        with patch("tempfile.mkdtemp", return_value=FAKE_DEST):
            result = await git_runner.run_clone(github_ref, FAKE_TOKEN, settings)

    assert result == FAKE_DEST
    mock_exec.assert_awaited_once()


async def test_run_clone_non_zero_exit_raises_git_execution_error(github_ref, settings):
    proc = _make_mock_process(returncode=128, stderr_bytes=b"fatal: repo not found")

    with patch("asyncio.create_subprocess_exec", new=AsyncMock(return_value=proc)):
        with patch("tempfile.mkdtemp", return_value=FAKE_DEST):
            with pytest.raises(GitExecutionError) as exc_info:
                await git_runner.run_clone(github_ref, FAKE_TOKEN, settings)

    assert exc_info.value.exit_code == 128


async def test_run_clone_non_zero_exit_token_not_in_exception(github_ref, settings):
    stderr_with_token = f"error: auth failed token={FAKE_TOKEN}".encode()
    proc = _make_mock_process(returncode=1, stderr_bytes=stderr_with_token)

    with patch("asyncio.create_subprocess_exec", new=AsyncMock(return_value=proc)):
        with patch("tempfile.mkdtemp", return_value=FAKE_DEST):
            with pytest.raises(GitExecutionError) as exc_info:
                await git_runner.run_clone(github_ref, FAKE_TOKEN, settings)

    assert FAKE_TOKEN not in str(exc_info.value)


async def test_run_clone_timeout_raises_clone_timeout_error(github_ref, settings):
    settings_fast = IngestionSettings(
        clone_workdir=settings.clone_workdir,
        clone_timeout_seconds=0.01,
    )

    async def slow_wait():
        await asyncio.sleep(10)

    proc = _make_mock_process(returncode=None)
    proc.wait = AsyncMock(side_effect=slow_wait)

    with patch("asyncio.create_subprocess_exec", new=AsyncMock(return_value=proc)):
        with patch("tempfile.mkdtemp", return_value=FAKE_DEST):
            with patch("os.killpg"):
                with patch("os.getpgid", return_value=12345):
                    with pytest.raises(CloneTimeoutError) as exc_info:
                        await git_runner.run_clone(github_ref, FAKE_TOKEN, settings_fast)

    assert exc_info.value.timeout_seconds == 0.01


async def test_run_clone_timeout_sends_sigterm(github_ref, settings):
    settings_fast = IngestionSettings(
        clone_workdir=settings.clone_workdir,
        clone_timeout_seconds=0.01,
    )

    async def slow_wait():
        await asyncio.sleep(10)

    proc = _make_mock_process(returncode=None)
    proc.wait = AsyncMock(side_effect=slow_wait)

    with patch("asyncio.create_subprocess_exec", new=AsyncMock(return_value=proc)):
        with patch("tempfile.mkdtemp", return_value=FAKE_DEST):
            with patch("os.killpg") as mock_killpg:
                with patch("os.getpgid", return_value=12345):
                    with pytest.raises(CloneTimeoutError):
                        await git_runner.run_clone(github_ref, FAKE_TOKEN, settings_fast)

    sent_signals = [call.args[1] for call in mock_killpg.call_args_list]
    assert signal.SIGTERM in sent_signals


async def test_run_clone_creates_missing_workdir(github_ref, tmp_path):
    missing_workdir = tmp_path / "cortexa-clones"
    settings_missing = IngestionSettings(
        clone_workdir=str(missing_workdir),
        clone_timeout_seconds=5.0,
    )
    proc = _make_mock_process(returncode=0)

    with patch("asyncio.create_subprocess_exec", new=AsyncMock(return_value=proc)):
        await git_runner.run_clone(github_ref, FAKE_TOKEN, settings_missing)

    assert missing_workdir.exists()


async def test_run_clone_workdir_used_for_temp_dest(github_ref, settings, tmp_path):
    proc = _make_mock_process(returncode=0)

    with patch("asyncio.create_subprocess_exec", new=AsyncMock(return_value=proc)):
        with patch("tempfile.mkdtemp", return_value=str(tmp_path / "clone1")) as mock_mkdtemp:
            await git_runner.run_clone(github_ref, FAKE_TOKEN, settings)

    mock_mkdtemp.assert_called_once_with(dir=str(tmp_path))
