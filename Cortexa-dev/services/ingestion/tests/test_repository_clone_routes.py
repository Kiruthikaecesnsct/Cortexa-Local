from datetime import UTC, datetime

import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient

from ingestion.api.routes.repository_clone_routes import router
from ingestion.domain.enums.clone_status import CloneStatus
from ingestion.domain.errors.github_scan_errors import (
    CloneNotFoundError,
    CloneStorageUnavailableError,
    MissingUserContextError,
)
from ingestion.domain.models.repository_clone import ArchiveDownload, RepositoryClone

TOKEN = "ghp_testtoken"
USER = "user-1"
CLONE_ID = "a" * 32
DOWNLOAD_URL = "/scan/github/clones/download?owner=acme&repository=api&branch=feature/x"
NOW = datetime(2026, 9, 30, tzinfo=UTC)
BODY = {"org_url": "https://github.com/acme", "pat": TOKEN, "repository": "api", "branch": "main"}


def _clone() -> RepositoryClone:
    return RepositoryClone(
        clone_id=CLONE_ID,
        owner="acme",
        repository="api",
        branch="main",
        status=CloneStatus.QUEUED,
        created_at=NOW,
        updated_at=NOW,
        saved_by=USER,
    )


class StubHandler:
    def __init__(self, error: Exception | None = None) -> None:
        self.error = error
        self.users: list[str | None] = []

    def _check(self, user_id: str | None) -> None:
        self.users.append(user_id)
        if self.error:
            raise self.error
        if user_id is None:
            raise MissingUserContextError("A signed-in user is required to manage clones.")

    async def start(self, body, user_id):  # noqa: ANN001, ANN201
        self._check(user_id)
        return _clone()

    async def list_clones(self, user_id):  # noqa: ANN001, ANN201
        self._check(user_id)
        return [_clone()]

    async def open_download(self, user_id, owner, repository, branch):  # noqa: ANN001, ANN201
        self.download_args = (owner, repository, branch)
        self._check(user_id)
        return ArchiveDownload(chunks=iter([b"PK", b"zip"]), size_bytes=5, filename="api.zip")


def _client(handler: StubHandler) -> TestClient:
    app = FastAPI()
    app.include_router(router)
    app.state.repository_clone_handler = handler
    return TestClient(app)


def test_start_returns_202_and_uses_gateway_user_header() -> None:
    handler = StubHandler()

    response = _client(handler).post("/scan/github/clones", json=BODY, headers={"X-User-Id": USER})

    assert response.status_code == 202
    data = response.json()["data"]
    assert data["clone_id"] == CLONE_ID
    assert "saved_by" not in data
    assert TOKEN not in response.text
    assert handler.users == [USER]


def test_missing_user_header_is_403_not_401() -> None:
    response = _client(StubHandler()).post("/scan/github/clones", json=BODY)

    assert response.status_code == 403
    assert response.json()["error_code"] == "missing_user_context"


def test_list_returns_clones() -> None:
    response = _client(StubHandler()).get("/scan/github/clones", headers={"X-User-Id": USER})

    assert response.status_code == 200
    assert response.json()["data"]["clones"][0]["status"] == "queued"


def test_download_streams_zip_with_filename() -> None:
    response = _client(StubHandler()).get(DOWNLOAD_URL, headers={"X-User-Id": USER})

    assert response.status_code == 200
    assert response.content == b"PKzip"
    assert response.headers["content-type"] == "application/zip"
    assert 'filename="api.zip"' in response.headers["content-disposition"]


@pytest.mark.parametrize(
    ("error", "status"),
    [(CloneNotFoundError("Clone not found."), 404), (CloneStorageUnavailableError("down"), 503)],
)
def test_download_errors_use_envelope(error: Exception, status: int) -> None:
    response = _client(StubHandler(error)).get(DOWNLOAD_URL, headers={"X-User-Id": USER})

    assert response.status_code == status
    assert response.json()["success"] is False


def test_storage_unavailable_on_list_is_503() -> None:
    response = _client(StubHandler(CloneStorageUnavailableError("down"))).get(
        "/scan/github/clones", headers={"X-User-Id": USER}
    )

    assert response.status_code == 503
    assert response.json()["error_code"] == "clone_storage_unavailable"


def test_download_passes_owner_repository_and_branch() -> None:
    handler = StubHandler()

    _client(handler).get(DOWNLOAD_URL, headers={"X-User-Id": USER})

    assert handler.download_args == ("acme", "api", "feature/x")


def test_download_requires_all_query_parameters() -> None:
    response = _client(StubHandler()).get(
        "/scan/github/clones/download?owner=acme", headers={"X-User-Id": USER}
    )

    assert response.status_code == 422
