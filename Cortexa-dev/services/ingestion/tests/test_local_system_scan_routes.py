import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient

from ingestion.api.routes.local_system_scan_routes import router
from ingestion.application.handlers.local_system_scan_handler import LocalSystemScanHandler
from ingestion.application.local_system_scan.local_system_port import SshConnectionParams
from ingestion.domain.errors.local_system_scan_errors import (
    LocalSystemAuthError,
    LocalSystemConnectionError,
    LocalSystemPathNotFoundError,
)
from ingestion.domain.models.local_system_scan import DirectoryEntry, DirectoryListing

PRIVATE_KEY = "-----BEGIN OPENSSH PRIVATE KEY-----\nfake\n-----END OPENSSH PRIVATE KEY-----"
BODY = {"host": "10.0.0.5", "username": "ubuntu", "private_key": PRIVATE_KEY, "path": "/var/data"}


class StubReader:
    def __init__(self, error: Exception | None = None) -> None:
        self._error = error

    async def list_directory(self, connection: SshConnectionParams, path: str) -> DirectoryListing:
        if self._error:
            raise self._error
        return DirectoryListing(
            path=path, entries=[DirectoryEntry(name="app", path=f"{path}/app", type="directory")]
        )


def _client(error: Exception | None = None) -> TestClient:
    app = FastAPI()
    app.include_router(router)
    app.state.local_system_scan_handler = LocalSystemScanHandler(StubReader(error))
    return TestClient(app)


def test_list_directory_returns_envelope_with_correlation_id() -> None:
    response = _client().post(
        "/scan/local-system/list", json=BODY, headers={"X-Correlation-Id": "cid-1"}
    )

    assert response.status_code == 200
    body = response.json()
    assert body["success"] is True
    assert body["correlation_id"] == "cid-1"
    assert body["data"]["entries"][0]["name"] == "app"


def test_list_directory_never_echoes_the_private_key() -> None:
    response = _client().post("/scan/local-system/list", json=BODY)

    assert PRIVATE_KEY not in response.text


@pytest.mark.parametrize(
    ("error", "status", "code"),
    [
        (LocalSystemAuthError("bad key"), 400, "local_system_auth_failed"),
        (LocalSystemConnectionError("unreachable"), 502, "local_system_connection_failed"),
        (LocalSystemPathNotFoundError("missing"), 404, "local_system_path_not_found"),
    ],
)
def test_list_directory_maps_errors_without_401(error: Exception, status: int, code: str) -> None:
    response = _client(error).post("/scan/local-system/list", json=BODY)

    assert response.status_code == status
    assert response.json()["error_code"] == code


def test_invalid_path_returns_422() -> None:
    response = _client().post("/scan/local-system/list", json={**BODY, "path": "relative/path"})

    assert response.status_code == 422
    assert response.json()["error_code"] == "invalid_scan_target"


def test_invalid_host_returns_422() -> None:
    response = _client().post("/scan/local-system/list", json={**BODY, "host": "bad_host!"})

    assert response.status_code == 422
    assert response.json()["error_code"] == "invalid_scan_target"
