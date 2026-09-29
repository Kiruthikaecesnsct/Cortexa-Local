from unittest.mock import AsyncMock, MagicMock, patch

import pytest

from vector_router.infrastructure.backends.ai_search_backend import (
    AiSearchBackend,
    AiSearchBackendConfig,
)
from vector_router.infrastructure.backends.qdrant_backend import QdrantBackend

ENDPOINT = "https://test-search.search.windows.net"
INDEX_NAME = "test-index"
DIMENSIONS = 3
COLLECTION_NAME = "test-corpus"
QDRANT_URL = "http://localhost:6333"


def _make_ai_search_backend(mock_credential, mock_embedder) -> AiSearchBackend:
    return AiSearchBackend(
        config=AiSearchBackendConfig(
            endpoint=ENDPOINT,
            index_name=INDEX_NAME,
            asset_index_name="test-asset-index",
            dimensions=DIMENSIONS,
        ),
        credential=mock_credential,
        embedder=mock_embedder,
    )


def _make_async_iter(items: list):
    async def _gen():
        for item in items:
            yield item

    return _gen()


def _build_ai_search_mock_client(raw_score: float):
    result = {"id": "doc-1", "@search.score": raw_score, "payload": None}
    mock_client = AsyncMock()
    mock_client.__aenter__ = AsyncMock(return_value=mock_client)
    mock_client.__aexit__ = AsyncMock(return_value=False)
    mock_client.search = AsyncMock(return_value=_make_async_iter([result]))
    return mock_client


def _make_qdrant_backend(mock_embedder) -> QdrantBackend:
    return QdrantBackend(
        url=QDRANT_URL,
        collection_name=COLLECTION_NAME,
        asset_collection_name="test-asset",
        dimensions=DIMENSIONS,
        embedder=mock_embedder,
    )


def _make_qdrant_scored_point(score: float):
    point = MagicMock()
    point.id = "point-1"
    point.score = score
    point.payload = {}
    return point


async def test_ai_search_bounded_score_passes_through_without_cosine_remap(
    mock_credential, mock_embedder
):
    raw_score = 0.66
    mock_client = _build_ai_search_mock_client(raw_score)
    backend = _make_ai_search_backend(mock_credential, mock_embedder)

    with patch.object(backend, "_client", return_value=mock_client):
        hits = await backend.search([0.1, 0.2, 0.3], top_k=5, filters=None)

    assert hits[0].score == pytest.approx(0.66)
    assert hits[0].score != pytest.approx((raw_score + 1.0) / 2.0)


async def test_qdrant_cosine_score_is_remapped_from_minus_one_one_to_zero_one(mock_embedder):
    raw_score = 0.66
    scored_point = _make_qdrant_scored_point(raw_score)
    backend = _make_qdrant_backend(mock_embedder)

    with patch.object(backend, "_client") as mock_client:
        mock_client.search = AsyncMock(return_value=[scored_point])
        hits = await backend.search([0.1, 0.2, 0.3], top_k=5, filters=None)

    assert hits[0].score == pytest.approx((raw_score + 1.0) / 2.0)
    assert hits[0].score == pytest.approx(0.83)


async def test_ai_search_and_qdrant_disagree_on_same_raw_value_by_design(
    mock_credential, mock_embedder
):
    raw_score = 0.66

    ai_search_client = _build_ai_search_mock_client(raw_score)
    ai_search_backend = _make_ai_search_backend(mock_credential, mock_embedder)
    with patch.object(ai_search_backend, "_client", return_value=ai_search_client):
        ai_search_hits = await ai_search_backend.search([0.1], top_k=1, filters=None)

    qdrant_backend = _make_qdrant_backend(mock_embedder)
    scored_point = _make_qdrant_scored_point(raw_score)
    with patch.object(qdrant_backend, "_client") as mock_qdrant_client:
        mock_qdrant_client.search = AsyncMock(return_value=[scored_point])
        qdrant_hits = await qdrant_backend.search([0.1], top_k=1, filters=None)

    assert ai_search_hits[0].score == pytest.approx(raw_score)
    assert qdrant_hits[0].score == pytest.approx((raw_score + 1.0) / 2.0)
    assert ai_search_hits[0].score != qdrant_hits[0].score
