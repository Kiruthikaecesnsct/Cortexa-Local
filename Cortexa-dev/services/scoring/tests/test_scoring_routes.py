from unittest.mock import AsyncMock

from fastapi import FastAPI
from fastapi.testclient import TestClient

from scoring.api.routes.scoring_routes import get_handler, router
from scoring.application.dtos.store_verdict_response import StoreVerdictResponseDto
from scoring.application.handlers.store_verdict_handler import StoreVerdictHandler
from scoring.domain.errors.storage_errors import EventPublishError, StorageWriteError


def _make_app(handler: StoreVerdictHandler) -> FastAPI:
    app = FastAPI()
    app.include_router(router)
    app.dependency_overrides[get_handler] = lambda: handler
    return app


def _mock_handler() -> StoreVerdictHandler:
    return AsyncMock(spec=StoreVerdictHandler)


_VALID_BODY = {
    "dual_verdict": {
        "primary": {
            "batch_id": "batch-1",
            "job_id": "job-1",
            "candidate_id": "cand-1",
            "document_id": "doc-1",
            "axes": {
                "Novelty": {"axis": "Novelty", "score": 80, "refs": []},
                "Inventiveness": {"axis": "Inventiveness", "score": 60, "refs": []},
                "Commercial": {"axis": "Commercial", "score": 70, "refs": []},
                "Strategic": {"axis": "Strategic", "score": 50, "refs": []},
                "Patentability": {"axis": "Patentability", "score": 90, "refs": []},
            },
        },
        "secondary": None,
        "agreement_level": "fallback_single",
        "agreeing_axis_count": 0,
    },
    "correlation_id": "corr-1",
}


def test_store_verdict_happy_path_returns_200():
    handler = _mock_handler()
    handler.handle = AsyncMock(
        return_value=StoreVerdictResponseDto(
            verdict_id="batch-1:cand-1",
            document_id="doc-1",
            published=True,
        )
    )
    client = TestClient(_make_app(handler))

    response = client.post("/verdicts/store", json=_VALID_BODY)

    assert response.status_code == 200
    body = response.json()
    assert body["published"] is True
    assert body["verdict_id"] == "batch-1:cand-1"


def test_store_verdict_event_publish_error_returns_502():
    handler = _mock_handler()
    handler.handle = AsyncMock(
        side_effect=EventPublishError("bus down", verdict_id="batch-1:cand-1", document_id="doc-1")
    )
    client = TestClient(_make_app(handler))

    response = client.post("/verdicts/store", json=_VALID_BODY)

    assert response.status_code == 502
    detail = response.json()["detail"]
    assert detail["error"] == "event_publish_failed"
    assert detail["verdict_id"] == "batch-1:cand-1"
    assert detail["document_id"] == "doc-1"


def test_store_verdict_storage_write_error_returns_500():
    handler = _mock_handler()
    handler.handle = AsyncMock(side_effect=StorageWriteError("cosmos down"))
    client = TestClient(_make_app(handler))

    response = client.post("/verdicts/store", json=_VALID_BODY)

    assert response.status_code == 500
