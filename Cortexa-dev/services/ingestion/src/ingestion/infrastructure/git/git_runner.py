import asyncio
import base64
import logging
import os
import re
import signal
import tempfile

from ingestion.domain.errors.clone_errors import CloneTimeoutError, GitExecutionError
from ingestion.domain.models.clone_request import RepoRef
from ingestion.infrastructure.config.settings import IngestionSettings

logger = logging.getLogger(__name__)

_TOKEN_PATTERN = re.compile(r"(?:ghp_|[A-Za-z0-9+/]{20,})[A-Za-z0-9+/=]*")


def _redact_stderr(text: str) -> str:
    return _TOKEN_PATTERN.sub("[REDACTED]", text)


def _build_clone_args(dest: str, url: str, branch: str | None) -> list[str]:
    args = [
        "git",
        "clone",
        "--depth=1",
        "--single-branch",
        "--no-tags",
    ]
    if branch:
        args.extend(["--branch", branch])
    args.extend([url, dest])
    return args


def _build_clone_env(token: str | None) -> dict[str, str]:
    env = os.environ.copy()
    if token:
        creds = base64.b64encode(f"x-token:{token}".encode()).decode()
        header_value = f"Authorization: Basic {creds}"
        env["GIT_CONFIG_COUNT"] = "1"
        env["GIT_CONFIG_KEY_0"] = "http.extraHeader"
        env["GIT_CONFIG_VALUE_0"] = header_value
    return env


async def run_clone(
    ref: RepoRef, token: str | None, settings: IngestionSettings, branch: str | None = None
) -> str:
    os.makedirs(settings.clone_workdir, exist_ok=True)
    dest = tempfile.mkdtemp(dir=settings.clone_workdir)
    args = _build_clone_args(dest, ref.normalized_https_url, branch)
    env = _build_clone_env(token)
    proc = await asyncio.create_subprocess_exec(
        *args,
        env=env,
        stdout=asyncio.subprocess.PIPE,
        stderr=asyncio.subprocess.PIPE,
        start_new_session=True,
    )
    try:
        await asyncio.wait_for(
            proc.wait(),
            timeout=settings.clone_timeout_seconds,
        )
    except TimeoutError:
        await _terminate(proc)
        raise CloneTimeoutError(settings.clone_timeout_seconds)

    if proc.returncode != 0:
        raw_stderr = (await proc.stderr.read()).decode(errors="replace") if proc.stderr else ""
        clean = _redact_stderr(raw_stderr)
        raise GitExecutionError(exit_code=proc.returncode, message=clean)

    return dest


async def _terminate(proc: asyncio.subprocess.Process) -> None:
    try:
        os.killpg(os.getpgid(proc.pid), signal.SIGTERM)
    except ProcessLookupError:
        return
    try:
        await asyncio.wait_for(proc.wait(), timeout=2.0)
    except TimeoutError:
        try:
            os.killpg(os.getpgid(proc.pid), signal.SIGKILL)
        except ProcessLookupError:
            pass
