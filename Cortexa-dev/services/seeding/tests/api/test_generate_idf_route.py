from fastapi import FastAPI
from fastapi.testclient import TestClient

from seeding.api.routes.seeding_routes import router
from seeding.application.handlers.generate_idf_handler import GenerateIdfDeps
from seeding.domain.errors.seeding_errors import (
    AbstractTooLongError,
    IdfParseError,
    ModelRouterFailedError,
    OpportunityParseError,
    UngroundedSeedingError,
)

_VALID_BODY = {
    "candidate_id": "cand-1",
    "batch_id": "batch-1",
    "job_id": "job-1",
    "document_id": "doc-1",
    "axes": {},
    "opportunities": {},
    "bundle_id": "bundle-1",
    "hits": [],
    "source_flags": [],
}


def _make_client(mocker, side_effect) -> TestClient:
    app = FastAPI()
    app.include_router(router, prefix="/seeding")
    mock_deps = mocker.MagicMock(spec=GenerateIdfDeps)
    app.state.generate_idf_deps = mock_deps
    mocker.patch(
        "seeding.api.routes.seeding_routes.generate_idf_handler.handle",
        side_effect=side_effect,
    )
    return TestClient(app, raise_server_exceptions=False)


def test_generate_idf_opportunity_parse_error_returns_422(mocker):
    client = _make_client(mocker, OpportunityParseError("no JSON object found"))
    resp = client.post("/seeding/generate-idf", json=_VALID_BODY)
    assert resp.status_code == 422
    body = resp.json()
    assert body["error_code"] == "OPPORTUNITY_PARSE_ERROR"
    assert body["success"] is False


def test_generate_idf_idf_parse_error_returns_422(mocker):
    client = _make_client(mocker, IdfParseError("bad idf"))
    resp = client.post("/seeding/generate-idf", json=_VALID_BODY)
    assert resp.status_code == 422
    assert resp.json()["error_code"] == "IDF_PARSE_ERROR"


def test_generate_idf_abstract_too_long_returns_422(mocker):
    client = _make_client(mocker, AbstractTooLongError("too long"))
    resp = client.post("/seeding/generate-idf", json=_VALID_BODY)
    assert resp.status_code == 422
    assert resp.json()["error_code"] == "ABSTRACT_TOO_LONG"


def test_generate_idf_ungrounded_returns_422(mocker):
    client = _make_client(mocker, UngroundedSeedingError("ungrounded"))
    resp = client.post("/seeding/generate-idf", json=_VALID_BODY)
    assert resp.status_code == 422
    assert resp.json()["error_code"] == "UNGROUNDED_SEEDING"


def test_generate_idf_model_router_failed_returns_502(mocker):
    client = _make_client(mocker, ModelRouterFailedError("upstream"))
    resp = client.post("/seeding/generate-idf", json=_VALID_BODY)
    assert resp.status_code == 502
    assert resp.json()["error_code"] == "MODEL_ROUTER_FAILED"
