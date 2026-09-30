import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient

from ingestion.api.routes.azure_devops_scan_routes import router
from ingestion.application.handlers.azure_devops_scan_handler import AzureDevOpsScanHandler
from ingestion.domain.errors.azure_devops_scan_errors import (
    AzureDevOpsAuthError,
    AzureDevOpsRateLimitError,
    AzureDevOpsUpstreamError,
)
from ingestion.domain.models.azure_devops_scan import (
    BranchSummary,
    RepositorySummary,
    RepositoryTree,
)

TOKEN = "azdo_testtoken"
BODY = {"org_url": "https://dev.azure.com/acme", "pat": TOKEN}


class StubReader:
    def __init__(self, error: Exception | None = None) -> None:
        self._error = error

    async def list_repositories(self, organization: str, token: str) -> list[RepositorySummary]:
        if self._error:
            raise self._error
        return [
            RepositorySummary(
                name="Project/api",
                full_name="acme/Project/api",
                html_url="https://dev.azure.com/acme/Project/_git/api",
            )
        ]

    async def list_branches(
        self, organization: str, project: str, repository: str, token: str
    ) -> list[BranchSummary]:
        if self._error:
            raise self._error
        return [BranchSummary(name="main", protected=True)]

    async def get_tree(
        self, organization: str, project: str, repository: str, branch: str, token: str
    ) -> RepositoryTree:
        if self._error:
            raise self._error
        return RepositoryTree(repository=f"{project}/{repository}", branch=branch, entries=[])


def _client(error: Exception | None = None) -> TestClient:
    app = FastAPI()
    app.include_router(router)
    app.state.azure_devops_scan_handler = AzureDevOpsScanHandler(StubReader(error))
    return TestClient(app)


def test_list_repositories_returns_envelope_with_correlation_id() -> None:
    response = _client().post(
        "/scan/azure-devops/repositories", json=BODY, headers={"X-Correlation-Id": "cid-1"}
    )

    assert response.status_code == 200
    body = response.json()
    assert body["success"] is True
    assert body["correlation_id"] == "cid-1"
    assert body["data"]["repositories"][0]["name"] == "Project/api"


@pytest.mark.parametrize(
    ("error", "status", "code"),
    [
        (AzureDevOpsAuthError("bad"), 400, "azure_devops_auth_failed"),
        (AzureDevOpsRateLimitError("slow"), 429, "azure_devops_rate_limited"),
        (AzureDevOpsUpstreamError("down"), 502, "azure_devops_upstream_error"),
    ],
)
def test_list_repositories_maps_errors_without_401(
    error: Exception, status: int, code: str
) -> None:
    response = _client(error).post("/scan/azure-devops/repositories", json=BODY)

    assert response.status_code == status
    assert response.json()["error_code"] == code
    assert TOKEN not in response.text


def test_invalid_org_url_returns_422() -> None:
    response = _client().post(
        "/scan/azure-devops/repositories", json={**BODY, "org_url": "https://evil.io/acme"}
    )

    assert response.status_code == 422
    assert response.json()["error_code"] == "invalid_scan_target"


def test_tree_endpoint_returns_tree() -> None:
    response = _client().post(
        "/scan/azure-devops/tree", json={**BODY, "repository": "Project/api", "branch": "main"}
    )

    assert response.status_code == 200
    assert response.json()["data"]["repository"] == "Project/api"


def test_branches_endpoint_returns_branches() -> None:
    response = _client().post(
        "/scan/azure-devops/branches", json={**BODY, "repository": "Project/api"}
    )

    assert response.status_code == 200
    data = response.json()["data"]
    assert data["repository"] == "Project/api"
    assert data["branches"][0] == {"name": "main", "protected": True, "commit_sha": None}


def test_branches_endpoint_maps_auth_error_to_400() -> None:
    response = _client(AzureDevOpsAuthError("bad")).post(
        "/scan/azure-devops/branches", json={**BODY, "repository": "Project/api"}
    )

    assert response.status_code == 400
    assert response.json()["error_code"] == "azure_devops_auth_failed"
