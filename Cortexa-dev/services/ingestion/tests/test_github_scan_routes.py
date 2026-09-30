import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient

from ingestion.api.routes.github_scan_routes import router
from ingestion.application.handlers.github_scan_handler import GitHubScanHandler
from ingestion.domain.errors.github_scan_errors import (
    GitHubAuthError,
    GitHubRateLimitError,
    GitHubUpstreamError,
)
from ingestion.domain.models.github_scan import BranchSummary, RepositorySummary, RepositoryTree

TOKEN = "ghp_testtoken"
BODY = {"org_url": "https://github.com/acme", "pat": TOKEN}


class StubReader:
    def __init__(self, error: Exception | None = None) -> None:
        self._error = error

    async def list_repositories(self, owner: str, token: str) -> list[RepositorySummary]:
        if self._error:
            raise self._error
        return [
            RepositorySummary(
                name="api",
                full_name="acme/api",
                private=False,
                default_branch="main",
                html_url="https://github.com/acme/api",
            )
        ]

    async def list_branches(self, owner: str, repo: str, token: str) -> list[BranchSummary]:
        if self._error:
            raise self._error
        return [BranchSummary(name="main", protected=True)]

    async def get_tree(self, owner: str, repo: str, branch: str, token: str) -> RepositoryTree:
        if self._error:
            raise self._error
        return RepositoryTree(repository=repo, branch=branch, entries=[])


def _client(error: Exception | None = None) -> TestClient:
    app = FastAPI()
    app.include_router(router)
    app.state.github_scan_handler = GitHubScanHandler(StubReader(error))
    return TestClient(app)


def test_list_repositories_returns_envelope_with_correlation_id() -> None:
    response = _client().post(
        "/scan/github/repositories", json=BODY, headers={"X-Correlation-Id": "cid-1"}
    )

    assert response.status_code == 200
    body = response.json()
    assert body["success"] is True
    assert body["correlation_id"] == "cid-1"
    assert body["data"]["repositories"][0]["name"] == "api"


@pytest.mark.parametrize(
    ("error", "status", "code"),
    [
        (GitHubAuthError("bad"), 400, "github_auth_failed"),
        (GitHubRateLimitError("slow"), 429, "github_rate_limited"),
        (GitHubUpstreamError("down"), 502, "github_upstream_error"),
    ],
)
def test_list_repositories_maps_errors_without_401(
    error: Exception, status: int, code: str
) -> None:
    response = _client(error).post("/scan/github/repositories", json=BODY)

    assert response.status_code == status
    assert response.json()["error_code"] == code
    assert TOKEN not in response.text


def test_invalid_org_url_returns_422() -> None:
    response = _client().post(
        "/scan/github/repositories", json={**BODY, "org_url": "https://evil.io/acme"}
    )

    assert response.status_code == 422
    assert response.json()["error_code"] == "invalid_scan_target"


def test_tree_endpoint_returns_tree() -> None:
    response = _client().post(
        "/scan/github/tree", json={**BODY, "repository": "api", "branch": "main"}
    )

    assert response.status_code == 200
    assert response.json()["data"]["repository"] == "api"


def test_branches_endpoint_returns_branches() -> None:
    response = _client().post("/scan/github/branches", json={**BODY, "repository": "api"})

    assert response.status_code == 200
    data = response.json()["data"]
    assert data["repository"] == "api"
    assert data["branches"][0] == {"name": "main", "protected": True, "commit_sha": None}


def test_branches_endpoint_maps_auth_error_to_400() -> None:
    response = _client(GitHubAuthError("bad")).post(
        "/scan/github/branches", json={**BODY, "repository": "api"}
    )

    assert response.status_code == 400
    assert response.json()["error_code"] == "github_auth_failed"
