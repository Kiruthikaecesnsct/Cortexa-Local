import logging
from collections.abc import Callable
from typing import Any
from urllib.parse import quote

import httpx

from ingestion.domain.errors.github_scan_errors import (
    GitHubAccessDeniedError,
    GitHubAuthError,
    GitHubEmptyRepositoryError,
    GitHubNotFoundError,
    GitHubRateLimitError,
    GitHubScanError,
    GitHubUpstreamError,
)
from ingestion.domain.models.github_scan import (
    BranchSummary,
    RepositorySummary,
    RepositoryTree,
    TreeEntry,
)
from ingestion.infrastructure.config.settings import IngestionSettings

_logger = logging.getLogger(__name__)

_PAGE_SIZE = 100
_ENTRY_TYPES = {"blob", "tree", "commit"}

_STATUS_ERRORS: dict[int, Callable[[], GitHubScanError]] = {
    401: lambda: GitHubAuthError("GitHub rejected the personal access token."),
    404: lambda: GitHubNotFoundError("GitHub resource not found, or the token cannot access it."),
    409: lambda: GitHubEmptyRepositoryError("The repository is empty."),
    451: lambda: GitHubAccessDeniedError("GitHub blocked access to this resource."),
}


def create_github_http_client(settings: IngestionSettings) -> httpx.AsyncClient:
    return httpx.AsyncClient(
        base_url=settings.github_api_base_url,
        timeout=settings.github_scan_timeout_seconds,
        headers={
            "Accept": "application/vnd.github+json",
            "X-GitHub-Api-Version": settings.github_api_version,
        },
    )


class GitHubApiClient:
    def __init__(self, http: httpx.AsyncClient, max_pages: int) -> None:
        self._http = http
        self._max_pages = max_pages

    async def list_repositories(self, owner: str, token: str) -> list[RepositorySummary]:
        try:
            return await self._paginate(f"/orgs/{owner}/repos", {"type": "all"}, token, _to_summary)
        except GitHubNotFoundError:
            return await self._list_account_repositories(owner, token)

    async def get_repository(self, owner: str, repo: str, token: str) -> RepositorySummary:
        return _to_summary(_json(await self._get(f"/repos/{owner}/{repo}", token)))

    async def list_branches(self, owner: str, repo: str, token: str) -> list[BranchSummary]:
        return await self._paginate(f"/repos/{owner}/{repo}/branches", {}, token, _to_branch)

    async def get_tree(self, owner: str, repo: str, branch: str, token: str) -> RepositoryTree:
        try:
            sha = await self._resolve_branch_commit(owner, repo, branch, token)
            response = await self._get(
                f"/repos/{owner}/{repo}/git/trees/{sha}", token, {"recursive": "1"}
            )
        except GitHubEmptyRepositoryError:
            return RepositoryTree(repository=repo, branch=branch, entries=[])
        body = _json(response)
        entries = [_to_entry(e) for e in body.get("tree", []) if e.get("type") in _ENTRY_TYPES]
        return RepositoryTree(
            repository=repo, branch=branch, entries=entries, truncated=bool(body.get("truncated"))
        )

    async def _list_account_repositories(self, owner: str, token: str) -> list[RepositorySummary]:
        # The URL may name a user rather than an organization. When it is the token's own
        # account, /user/repos also returns the private repositories the token can see.
        login = str(_json(await self._get("/user", token)).get("login", ""))
        if login.lower() == owner.lower():
            return await self._paginate("/user/repos", {"affiliation": "owner"}, token, _to_summary)
        return await self._paginate(f"/users/{owner}/repos", {"type": "owner"}, token, _to_summary)

    async def _resolve_branch_commit(self, owner: str, repo: str, branch: str, token: str) -> str:
        ref_path = f"/repos/{owner}/{repo}/git/ref/heads/{quote(branch, safe='/')}"
        body = _json(await self._get(ref_path, token))
        sha = body.get("object", {}).get("sha")
        if not isinstance(sha, str):
            raise GitHubUpstreamError("GitHub returned an unexpected branch reference.")
        return sha

    async def _paginate[T](
        self,
        path: str,
        params: dict[str, str],
        token: str,
        mapper: Callable[[dict[str, Any]], T],
    ) -> list[T]:
        url: str | None = path
        query: dict[str, Any] | None = {**params, "per_page": _PAGE_SIZE}
        results: list[T] = []
        for _ in range(self._max_pages):
            response = await self._get(url, token, query)
            results.extend(mapper(item) for item in _json(response))
            url, query = self._next_link(response), None
            if url is None:
                return results
        _logger.warning("GitHub listing of %s stopped at %d pages", path, self._max_pages)
        return results

    def _next_link(self, response: httpx.Response) -> str | None:
        next_url = response.links.get("next", {}).get("url")
        # Only follow pagination links on the configured GitHub API origin; the PAT rides along.
        if next_url and _same_origin(httpx.URL(next_url), self._http.base_url):
            return next_url
        return None

    async def _get(
        self, url: str, token: str, params: dict[str, Any] | None = None
    ) -> httpx.Response:
        try:
            response = await self._http.get(
                url, params=params, headers={"Authorization": f"Bearer {token}"}
            )
        except httpx.HTTPError as exc:
            raise GitHubUpstreamError("GitHub could not be reached.") from exc
        _raise_for_status(response)
        return response


def _same_origin(candidate: httpx.URL, base: httpx.URL) -> bool:
    return (candidate.scheme, candidate.host, candidate.port) == (base.scheme, base.host, base.port)


def _raise_for_status(response: httpx.Response) -> None:
    if response.is_success:
        return
    if _is_rate_limited(response):
        raise GitHubRateLimitError("GitHub API rate limit reached. Try again later.")
    if response.status_code == 403:
        raise _access_denied(response)
    factory = _STATUS_ERRORS.get(response.status_code)
    if factory is not None:
        raise factory()
    raise GitHubUpstreamError(f"GitHub returned HTTP {response.status_code}.")


def _is_rate_limited(response: httpx.Response) -> bool:
    if response.status_code == 429:
        return True
    return response.status_code == 403 and response.headers.get("x-ratelimit-remaining") == "0"


def _access_denied(response: httpx.Response) -> GitHubAccessDeniedError:
    if "x-github-sso" in response.headers:
        return GitHubAccessDeniedError(
            "This organization requires SAML SSO authorization for the token."
        )
    return GitHubAccessDeniedError("The token does not have permission to read this resource.")


def _json(response: httpx.Response) -> Any:
    try:
        return response.json()
    except ValueError as exc:
        raise GitHubUpstreamError("GitHub returned a malformed response.") from exc


def _to_summary(item: dict[str, Any]) -> RepositorySummary:
    return RepositorySummary(
        name=item.get("name", ""),
        full_name=item.get("full_name", ""),
        private=bool(item.get("private")),
        default_branch=item.get("default_branch"),
        html_url=item.get("html_url", ""),
        description=item.get("description"),
        size_kb=item.get("size") or 0,
        updated_at=item.get("updated_at"),
    )


def _to_branch(item: dict[str, Any]) -> BranchSummary:
    return BranchSummary(
        name=item.get("name", ""),
        protected=bool(item.get("protected")),
        commit_sha=(item.get("commit") or {}).get("sha"),
    )


def _to_entry(item: dict[str, Any]) -> TreeEntry:
    return TreeEntry(path=item.get("path", ""), type=item["type"], size=item.get("size"))
