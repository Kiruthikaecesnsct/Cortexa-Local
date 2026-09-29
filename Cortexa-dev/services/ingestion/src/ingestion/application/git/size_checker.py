import base64

import httpx

from ingestion.domain.enums.git_host import GitHost
from ingestion.domain.errors.clone_errors import RepoTooLargeError
from ingestion.domain.models.clone_request import RepoRef
from ingestion.infrastructure.config.settings import IngestionSettings


async def check_size(ref: RepoRef, token: str | None, settings: IngestionSettings) -> None:
    size_bytes = await _fetch_size(ref, token)
    if size_bytes > settings.clone_max_repo_bytes:
        raise RepoTooLargeError(size_bytes, settings.clone_max_repo_bytes)


async def _fetch_size(ref: RepoRef, token: str | None) -> int:
    if ref.host == GitHost.GITHUB:
        return await _github_size(ref, token)
    return await _azdo_size(ref, token)


async def _github_size(ref: RepoRef, token: str | None) -> int:
    url = f"https://api.github.com/repos/{ref.owner}/{ref.repo}"
    headers = {"Accept": "application/vnd.github+json"}
    if token:
        headers["Authorization"] = f"token {token}"
    async with httpx.AsyncClient(timeout=30) as client:
        response = await client.get(url, headers=headers)
        response.raise_for_status()
        data = response.json()
    size_kb: int = data.get("size", 0)
    return size_kb * 1024


async def _azdo_size(ref: RepoRef, token: str | None) -> int:
    parts = ref.repo.split("/", 1)
    project, repo_name = parts[0], parts[1]
    url = (
        f"https://dev.azure.com/{ref.owner}/{project}/_apis/git/repositories/{repo_name}"
        "?api-version=7.1"
    )
    headers = {}
    if token:
        creds = base64.b64encode(f":{token}".encode()).decode()
        headers["Authorization"] = f"Basic {creds}"
    async with httpx.AsyncClient(timeout=30) as client:
        response = await client.get(url, headers=headers)
        response.raise_for_status()
        data = response.json()
    return int(data.get("size", 0))
