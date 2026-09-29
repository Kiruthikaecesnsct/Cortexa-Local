from unittest.mock import AsyncMock, MagicMock, patch

import pytest
from qdrant_client.models import Distance, Filter, PointStruct

from vector_router.domain.models import SearchFilter, VectorItem, VectorTarget
from vector_router.infrastructure.backends.qdrant_backend import QdrantBackend

COLLECTION_NAME = "test-corpus"
ASSET_COLLECTION_NAME = "test-asset"
QDRANT_URL = "http://localhost:6333"
DIMENSIONS = 3


def _make_backend(mock_embedder) -> QdrantBackend:
    backend = QdrantBackend(
        url=QDRANT_URL,
        collection_name=COLLECTION_NAME,
        asset_collection_name=ASSET_COLLECTION_NAME,
        dimensions=DIMENSIONS,
        embedder=mock_embedder,
    )
    return backend


def _make_scored_point(point_id: str, score: float, payload: dict):
    point = MagicMock()
    point.id = point_id
    point.score = score
    point.payload = payload
    return point


async def test_search_returns_vector_hits_with_correct_fields(mock_embedder):
    scored_point = _make_scored_point("point-1", 0.6, {"title": "Research Paper"})
    backend = _make_backend(mock_embedder)

    with patch.object(backend, "_client") as mock_client:
        mock_client.search = AsyncMock(return_value=[scored_point])
        hits = await backend.search([0.1, 0.2, 0.3], top_k=5, filters=None)

    assert len(hits) == 1
    assert hits[0].id == "point-1"
    assert hits[0].payload == {"title": "Research Paper"}


async def test_search_normalizes_score_one_to_one(mock_embedder):
    scored_point = _make_scored_point("p", 1.0, {})
    backend = _make_backend(mock_embedder)

    with patch.object(backend, "_client") as mock_client:
        mock_client.search = AsyncMock(return_value=[scored_point])
        hits = await backend.search([0.1], top_k=1, filters=None)

    assert hits[0].score == pytest.approx(1.0)


async def test_search_normalizes_score_minus_one_to_zero(mock_embedder):
    scored_point = _make_scored_point("p", -1.0, {})
    backend = _make_backend(mock_embedder)

    with patch.object(backend, "_client") as mock_client:
        mock_client.search = AsyncMock(return_value=[scored_point])
        hits = await backend.search([0.1], top_k=1, filters=None)

    assert hits[0].score == pytest.approx(0.0)


async def test_search_normalizes_score_zero_to_half(mock_embedder):
    scored_point = _make_scored_point("p", 0.0, {})
    backend = _make_backend(mock_embedder)

    with patch.object(backend, "_client") as mock_client:
        mock_client.search = AsyncMock(return_value=[scored_point])
        hits = await backend.search([0.1], top_k=1, filters=None)

    assert hits[0].score == pytest.approx(0.5)


async def test_search_with_filters_passes_filter_object_to_sdk(mock_embedder):
    backend = _make_backend(mock_embedder)
    filters = [SearchFilter(field="batch_id", value="batch-42")]

    with patch.object(backend, "_client") as mock_client:
        mock_client.search = AsyncMock(return_value=[])
        await backend.search([0.1], top_k=5, filters=filters)

    call_kwargs = mock_client.search.call_args.kwargs
    assert isinstance(call_kwargs["query_filter"], Filter)
    assert call_kwargs["query_filter"].must[0].key == "batch_id"
    assert call_kwargs["query_filter"].must[0].match.value == "batch-42"


async def test_search_with_no_filters_passes_none_to_sdk(mock_embedder):
    backend = _make_backend(mock_embedder)

    with patch.object(backend, "_client") as mock_client:
        mock_client.search = AsyncMock(return_value=[])
        await backend.search([0.1], top_k=5, filters=None)

    call_kwargs = mock_client.search.call_args.kwargs
    assert call_kwargs["query_filter"] is None


async def test_upsert_builds_point_structs_and_returns_item_count(mock_embedder):
    backend = _make_backend(mock_embedder)
    items = [
        VectorItem(id="a", vector=[0.1, 0.2], payload={"k": "v"}),
        VectorItem(id="b", vector=[0.3, 0.4], payload={"k": "w"}),
    ]

    with patch.object(backend, "_client") as mock_client:
        mock_client.upsert = AsyncMock()
        count = await backend.upsert(items)

    assert count == 2
    upsert_call = mock_client.upsert.call_args
    points = upsert_call.kwargs["points"]
    assert len(points) == 2
    assert all(isinstance(p, PointStruct) for p in points)
    assert points[0].id == "a"
    assert points[1].id == "b"


async def test_embed_delegates_to_embedder(mock_embedder):
    backend = _make_backend(mock_embedder)

    result = await backend.embed(["hello"])

    mock_embedder.embed.assert_awaited_once_with(["hello"])
    assert result == [[0.1, 0.1, 0.1]]


async def test_search_default_target_uses_corpus_collection(mock_embedder):
    backend = _make_backend(mock_embedder)

    with patch.object(backend, "_client") as mock_client:
        mock_client.search = AsyncMock(return_value=[])
        await backend.search([0.1], top_k=5, filters=None)

    assert mock_client.search.call_args.kwargs["collection_name"] == COLLECTION_NAME


async def test_search_asset_target_uses_asset_collection(mock_embedder):
    backend = _make_backend(mock_embedder)

    with patch.object(backend, "_client") as mock_client:
        mock_client.search = AsyncMock(return_value=[])
        await backend.search([0.1], top_k=5, filters=None, target=VectorTarget.ASSET)

    assert mock_client.search.call_args.kwargs["collection_name"] == ASSET_COLLECTION_NAME


async def test_upsert_asset_target_uses_asset_collection(mock_embedder):
    backend = _make_backend(mock_embedder)
    items = [VectorItem(id="a", vector=[0.1, 0.2], payload={"batch_id": "b1"})]

    with patch.object(backend, "_client") as mock_client:
        mock_client.upsert = AsyncMock()
        await backend.upsert(items, target=VectorTarget.ASSET)

    assert mock_client.upsert.call_args.kwargs["collection_name"] == ASSET_COLLECTION_NAME


async def test_delete_asset_batch_deletes_matching_points_and_returns_count(mock_embedder):
    backend = _make_backend(mock_embedder)

    with patch.object(backend, "_client") as mock_client:
        mock_client.count = AsyncMock(return_value=MagicMock(count=3))
        mock_client.delete = AsyncMock()
        deleted = await backend.delete_asset_batch("batch-42")

    assert deleted == 3
    assert mock_client.count.call_args.kwargs["collection_name"] == ASSET_COLLECTION_NAME
    count_filter = mock_client.count.call_args.kwargs["count_filter"]
    assert isinstance(count_filter, Filter)
    assert count_filter.must[0].key == "batch_id"
    assert count_filter.must[0].match.value == "batch-42"
    mock_client.delete.assert_awaited_once()
    assert mock_client.delete.call_args.kwargs["collection_name"] == ASSET_COLLECTION_NAME


async def test_delete_asset_batch_empty_returns_zero_without_deleting(mock_embedder):
    backend = _make_backend(mock_embedder)

    with patch.object(backend, "_client") as mock_client:
        mock_client.count = AsyncMock(return_value=MagicMock(count=0))
        mock_client.delete = AsyncMock()
        deleted = await backend.delete_asset_batch("missing-batch")

    assert deleted == 0
    mock_client.delete.assert_not_awaited()


async def test_ensure_asset_store_creates_collection_when_missing(mock_embedder):
    backend = _make_backend(mock_embedder)

    with patch.object(backend, "_client") as mock_client:
        mock_client.collection_exists = AsyncMock(return_value=False)
        mock_client.create_collection = AsyncMock()
        await backend.ensure_asset_store()

    mock_client.collection_exists.assert_awaited_once_with(ASSET_COLLECTION_NAME)
    create_kwargs = mock_client.create_collection.call_args.kwargs
    assert create_kwargs["collection_name"] == ASSET_COLLECTION_NAME
    assert create_kwargs["vectors_config"].size == DIMENSIONS
    assert create_kwargs["vectors_config"].distance == Distance.COSINE


async def test_ensure_asset_store_skips_when_collection_exists(mock_embedder):
    backend = _make_backend(mock_embedder)

    with patch.object(backend, "_client") as mock_client:
        mock_client.collection_exists = AsyncMock(return_value=True)
        mock_client.create_collection = AsyncMock()
        await backend.ensure_asset_store()

    mock_client.create_collection.assert_not_awaited()
