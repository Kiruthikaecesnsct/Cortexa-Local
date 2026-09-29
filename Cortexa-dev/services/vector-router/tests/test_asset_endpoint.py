from unittest.mock import AsyncMock

from fastapi.testclient import TestClient

from vector_router.api.dependencies import get_router
from vector_router.domain.models import VectorTarget
from vector_router.main import create_app


def _client_with_router(mock_router: AsyncMock) -> TestClient:
    app = create_app()
    app.dependency_overrides[get_router] = lambda: mock_router
    return TestClient(app)


def test_delete_asset_endpoint_returns_deleted_count():
    mock_router = AsyncMock()
    mock_router.delete_asset_batch = AsyncMock(return_value=5)
    client = _client_with_router(mock_router)

    response = client.delete("/asset/batch-42")

    assert response.status_code == 200
    assert response.json() == {"deleted": 5}
    mock_router.delete_asset_batch.assert_awaited_once_with("batch-42")


def test_search_endpoint_defaults_target_to_corpus():
    mock_router = AsyncMock()
    mock_router.search = AsyncMock(return_value=[])
    client = _client_with_router(mock_router)

    response = client.post("/search", json={"embedding": [0.1, 0.2], "top_k": 5})

    assert response.status_code == 200
    assert mock_router.search.call_args.kwargs["target"] == VectorTarget.CORPUS


def test_search_endpoint_forwards_asset_target():
    mock_router = AsyncMock()
    mock_router.search = AsyncMock(return_value=[])
    client = _client_with_router(mock_router)

    response = client.post(
        "/search",
        json={"embedding": [0.1, 0.2], "top_k": 5, "target": "asset"},
    )

    assert response.status_code == 200
    assert mock_router.search.call_args.kwargs["target"] == VectorTarget.ASSET


def test_upsert_endpoint_forwards_asset_target():
    mock_router = AsyncMock()
    mock_router.upsert = AsyncMock(return_value=1)
    client = _client_with_router(mock_router)

    response = client.post(
        "/upsert",
        json={
            "items": [{"id": "a", "vector": [0.1], "payload": {"batch_id": "b1"}}],
            "target": "asset",
        },
    )

    assert response.status_code == 200
    assert response.json() == {"upserted": 1}
    assert mock_router.upsert.call_args.kwargs["target"] == VectorTarget.ASSET
