from datetime import UTC, datetime

import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient

from ingestion.api.routes.repository_clone_routes import (
    azure_devops_clone_router,
    github_clone_router,
)
from ingestion.domain.enums.clone_status import CloneStatus
from ingestion.domain.enums.source_provider import SourceProvider
from ingestion.domain.errors.scan_errors import (
    CloneNotFoundError,
    CloneStorageUnavailableError,
    MissingUserContextError,
)
from ingestion.domain.models.repository_clone import ArchiveDownload, RepositoryClone

TOKEN = "ghp_testtoken"
USER = "user-1"
CLONE_ID = "a" * 32
NOW = datetime(2026, 9, 30, tzinfo=UTC)
GITHUB_BODY = {
    "org_url": "https://github.com/acme",
    "pat": TOKEN,
    "repository": "api",
    "branch": "main",
}
AZURE_BODY = {
    "org_url": "https://dev.azure.com/contoso",
    "pat": TOKEN,
    "repository": "Platform/api",
    "branch": "main",
}
GITHUB_DOWNLOAD = "/scan/github/clones/download?owner=acme&repository=api&branch=feature/x"
AZURE_DOWNLOAD = (
    "/scan/azure-devops/clones/download?owner=contoso&repository=Platform%2Fapi&branch=main"
)
HEADERS = {"X-User-Id": USER}


def _clone(provider: SourceProvider) -> RepositoryClone:
    return RepositoryClone(
        clone_id=CLONE_ID,
        provider=provider,
        owner="acme",
        repository="api",
        branch="main",
        status=CloneStatus.QUEUED,
        created_at=NOW,
        updated_at=NOW,
        saved_by=USER,
    )


class StubHandler:
    def __init__(self, provider: SourceProvider, error: Exception | None = None) -> None:
        self.provider = provider
        self.error = error
        self.users: list[str | None] = []
        self.bodies: list[object] = []
        self.download_args: tuple[str, str, str] | None = None
        self.filename = "api.zip"

    def _check(self, user_id: str | None) -> None:
        self.users.append(user_id)
        if self.error:
            raise self.error
        if user_id is None:
            raise MissingUserContextError("Sign in to save repositories.")

    async def start(self, body, user_id):  # noqa: ANN001, ANN201
        self.bodies.append(body)
        self._check(user_id)
        return _clone(self.provider)

    async def list_clones(self, user_id):  # noqa: ANN001, ANN201
        self._check(user_id)
        return [_clone(self.provider)]

    async def open_download(self, user_id, owner, repository, branch):  # noqa: ANN001, ANN201
        self.download_args = (owner, repository, branch)
        self._check(user_id)
        return ArchiveDownload(chunks=iter([b"PK", b"zip"]), size_bytes=5, filename=self.filename)


class Stubs:
    def __init__(self, error: Exception | None = None) -> None:
        self.github = StubHandler(SourceProvider.GITHUB, error)
        self.azure = StubHandler(SourceProvider.AZURE_DEVOPS, error)
        app = FastAPI()
        app.include_router(github_clone_router)
        app.include_router(azure_devops_clone_router)
        app.state.repository_clone_handler = self.github
        app.state.azure_devops_clone_handler = self.azure
        self.client = TestClient(app)


def test_github_start_returns_202_and_uses_gateway_user_header() -> None:
    stubs = Stubs()

    response = stubs.client.post("/scan/github/clones", json=GITHUB_BODY, headers=HEADERS)

    assert response.status_code == 202
    data = response.json()["data"]
    assert (data["clone_id"], data["provider"]) == (CLONE_ID, "github")
    assert "saved_by" not in data
    assert TOKEN not in response.text
    assert stubs.github.users == [USER]
    assert stubs.azure.users == []


def test_azure_start_routes_to_azure_handler() -> None:
    stubs = Stubs()

    response = stubs.client.post("/scan/azure-devops/clones", json=AZURE_BODY, headers=HEADERS)

    assert response.status_code == 202
    assert response.json()["data"]["provider"] == "azure-devops"
    assert stubs.azure.bodies[0].repository == "Platform/api"
    assert stubs.github.users == []


def test_missing_user_header_is_403_not_401() -> None:
    response = Stubs().client.post("/scan/azure-devops/clones", json=AZURE_BODY)

    assert response.status_code == 403
    assert response.json()["error_code"] == "missing_user_context"


@pytest.mark.parametrize("path", ["/scan/github/clones", "/scan/azure-devops/clones"])
def test_list_returns_clones(path: str) -> None:
    response = Stubs().client.get(path, headers=HEADERS)

    assert response.status_code == 200
    assert response.json()["data"]["clones"][0]["status"] == "queued"


def test_download_streams_zip_with_filename() -> None:
    response = Stubs().client.get(GITHUB_DOWNLOAD, headers=HEADERS)

    assert response.status_code == 200
    assert response.content == b"PKzip"
    assert response.headers["content-type"] == "application/zip"
    assert 'filename="api.zip"' in response.headers["content-disposition"]


def test_azure_download_passes_project_repository() -> None:
    stubs = Stubs()

    stubs.client.get(AZURE_DOWNLOAD, headers=HEADERS)

    assert stubs.azure.download_args == ("contoso", "Platform/api", "main")


def test_download_header_survives_unusual_repository_names() -> None:
    stubs = Stubs()
    stubs.azure.filename = 'my "repo" ü.zip'

    response = stubs.client.get(AZURE_DOWNLOAD, headers=HEADERS)

    disposition = response.headers["content-disposition"]
    assert 'filename="my _repo_ _.zip"' in disposition
    assert "filename*=UTF-8''my%20%22repo%22%20%C3%BC.zip" in disposition


@pytest.mark.parametrize(
    ("error", "status"),
    [(CloneNotFoundError("missing"), 404), (CloneStorageUnavailableError("down"), 503)],
)
def test_download_errors_use_envelope(error: Exception, status: int) -> None:
    response = Stubs(error).client.get(AZURE_DOWNLOAD, headers=HEADERS)

    assert response.status_code == status
    assert response.json()["success"] is False


def test_storage_unavailable_on_list_is_503() -> None:
    response = Stubs(CloneStorageUnavailableError("down")).client.get(
        "/scan/github/clones", headers=HEADERS
    )

    assert response.status_code == 503
    assert response.json()["error_code"] == "clone_storage_unavailable"


def test_download_requires_all_query_parameters() -> None:
    response = Stubs().client.get("/scan/azure-devops/clones/download?owner=a", headers=HEADERS)

    assert response.status_code == 422
