import base64
import logging
from collections.abc import Callable
from typing import Any
from urllib.parse import quote

import httpx

from ingestion.domain.errors.azure_devops_scan_errors import (
    AzureDevOpsAccessDeniedError,
    AzureDevOpsAuthError,
    AzureDevOpsNotFoundError,
    AzureDevOpsRateLimitError,
    AzureDevOpsScanError,
    AzureDevOpsUpstreamError,
)
from ingestion.domain.models.azure_devops_scan import (
    BranchSummary,
    RepositorySummary,
    RepositoryTree,
    TreeEntry,
)
from ingestion.infrastructure.config.settings import IngestionSettings

_logger = logging.getLogger(__name__)

_HEADS_PREFIX = "refs/heads/"
_ROOT_PATHS = {"", "/"}

_STATUS_ERRORS: dict[int, Callable[[], AzureDevOpsScanError]] = {
    401: lambda: AzureDevOpsAuthError("Azure DevOps rejected the personal access token."),
    404: lambda: AzureDevOpsNotFoundError(
        "Azure DevOps resource not found, or the token cannot access it."
    ),
}


def create_azure_devops_http_client(settings: IngestionSettings) -> httpx.AsyncClient:
    return httpx.AsyncClient(
        base_url=settings.azdo_api_base_url,
        timeout=settings.azdo_scan_timeout_seconds,
        headers={"Accept": "application/json"},
        # A bad or expired PAT often surfaces as a 302 to the sign-in page rather than
        # a clean 401; never follow it, or the PAT would leak to Microsoft's login host.
        follow_redirects=False,
    )


def _basic_auth_header(token: str) -> str:
    return "Basic " + base64.b64encode(f":{token}".encode()).decode()


class AzureDevOpsApiClient:
    def __init__(self, http: httpx.AsyncClient, api_version: str) -> None:
        self._http = http
        self._api_version = api_version

    async def list_repositories(self, organization: str, token: str) -> list[RepositorySummary]:
        response = await self._get(f"/{organization}/_apis/git/repositories", token)
        items = _json(response).get("value", [])
        return [_to_summary(organization, item) for item in items]

    async def get_repository(
        self, organization: str, project: str, repository: str, token: str
    ) -> RepositorySummary:
        path = self._repo_path(organization, project, repository, "").rstrip("/")
        return _to_summary(organization, _json(await self._get(path, token)))

    async def list_branches(
        self, organization: str, project: str, repository: str, token: str
    ) -> list[BranchSummary]:
        path = self._repo_path(organization, project, repository, "refs")
        response = await self._get(path, token, {"filter": "heads/"})
        items = _json(response).get("value", [])
        return [_to_branch(item) for item in items]

    async def get_tree(
        self, organization: str, project: str, repository: str, branch: str, token: str
    ) -> RepositoryTree:
        path = self._repo_path(organization, project, repository, "items")
        params = {
            "recursionLevel": "Full",
            "versionDescriptor.version": branch,
            "versionDescriptor.versionType": "branch",
        }
        try:
            response = await self._get(path, token, params)
        except AzureDevOpsNotFoundError:
            # An empty repository (no commits yet) has no such branch ref to list items
            # from, which Azure DevOps also reports as 404.
            return RepositoryTree(repository=repository, branch=branch, entries=[])
        items = _json(response).get("value", [])
        entries = [_to_entry(i) for i in items if i.get("path") not in _ROOT_PATHS]
        return RepositoryTree(repository=repository, branch=branch, entries=entries)

    def _repo_path(self, organization: str, project: str, repository: str, resource: str) -> str:
        org, proj, repo = quote(organization), quote(project), quote(repository)
        return f"/{org}/{proj}/_apis/git/repositories/{repo}/{resource}"

    async def _get(
        self, url: str, token: str, params: dict[str, Any] | None = None
    ) -> httpx.Response:
        query = {**(params or {}), "api-version": self._api_version}
        try:
            response = await self._http.get(
                url, params=query, headers={"Authorization": _basic_auth_header(token)}
            )
        except httpx.HTTPError as exc:
            raise AzureDevOpsUpstreamError("Azure DevOps could not be reached.") from exc
        _raise_for_status(response)
        return response


def _raise_for_status(response: httpx.Response) -> None:
    if response.is_success:
        return
    if response.is_redirect:
        raise AzureDevOpsAuthError("Azure DevOps rejected the personal access token.")
    if response.status_code == 429:
        raise AzureDevOpsRateLimitError("Azure DevOps API rate limit reached. Try again later.")
    if response.status_code == 403:
        raise AzureDevOpsAccessDeniedError(
            "The token does not have permission to read this resource."
        )
    factory = _STATUS_ERRORS.get(response.status_code)
    if factory is not None:
        raise factory()
    raise AzureDevOpsUpstreamError(f"Azure DevOps returned HTTP {response.status_code}.")


def _json(response: httpx.Response) -> Any:
    try:
        return response.json()
    except ValueError as exc:
        raise AzureDevOpsUpstreamError("Azure DevOps returned a malformed response.") from exc


def _to_summary(organization: str, item: dict[str, Any]) -> RepositorySummary:
    project = (item.get("project") or {}).get("name", "")
    repo_name = item.get("name", "")
    default_branch = item.get("defaultBranch")
    if isinstance(default_branch, str) and default_branch.startswith(_HEADS_PREFIX):
        default_branch = default_branch[len(_HEADS_PREFIX) :]
    return RepositorySummary(
        name=f"{project}/{repo_name}",
        full_name=f"{organization}/{project}/{repo_name}",
        default_branch=default_branch,
        html_url=item.get("webUrl", ""),
        size_kb=(item.get("size") or 0) // 1024,
    )


def _to_branch(item: dict[str, Any]) -> BranchSummary:
    name = item.get("name", "")
    if name.startswith(_HEADS_PREFIX):
        name = name[len(_HEADS_PREFIX) :]
    return BranchSummary(name=name, commit_sha=item.get("objectId"))


def _to_entry(item: dict[str, Any]) -> TreeEntry:
    path = item.get("path", "").lstrip("/")
    return TreeEntry(path=path, type="tree" if item.get("isFolder") else "blob")
