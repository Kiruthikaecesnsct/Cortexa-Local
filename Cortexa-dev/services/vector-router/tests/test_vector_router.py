from unittest.mock import AsyncMock

from vector_router.application.router import VectorRouter
from vector_router.domain.models import SearchFilter, VectorHit, VectorItem, VectorTarget


def _make_mock_backend() -> AsyncMock:
    backend = AsyncMock()
    backend.search = AsyncMock()
    backend.upsert = AsyncMock()
    backend.delete_asset_batch = AsyncMock()
    backend.embed = AsyncMock()
    return backend


async def test_search_delegates_to_backend_with_same_arguments():
    embedding = [0.1, 0.2, 0.3]
    filters = [SearchFilter(field="field", value="val")]
    expected_hits = [VectorHit(id="doc-1", score=0.9, payload={})]
    mock_backend = _make_mock_backend()
    mock_backend.search.return_value = expected_hits
    router = VectorRouter(backend=mock_backend)

    result = await router.search(embedding, top_k=5, filters=filters)

    mock_backend.search.assert_awaited_once_with(embedding, 5, filters, target=VectorTarget.CORPUS)
    assert result == expected_hits


async def test_search_with_no_filters_delegates_none_to_backend():
    mock_backend = _make_mock_backend()
    mock_backend.search.return_value = []
    router = VectorRouter(backend=mock_backend)

    await router.search([0.1], top_k=3, filters=None)

    mock_backend.search.assert_awaited_once_with([0.1], 3, None, target=VectorTarget.CORPUS)


async def test_search_forwards_asset_target_to_backend():
    mock_backend = _make_mock_backend()
    mock_backend.search.return_value = []
    router = VectorRouter(backend=mock_backend)

    await router.search([0.1], top_k=3, filters=None, target=VectorTarget.ASSET)

    mock_backend.search.assert_awaited_once_with([0.1], 3, None, target=VectorTarget.ASSET)


async def test_upsert_delegates_to_backend_and_returns_count():
    items = [VectorItem(id="a", vector=[0.1], payload={})]
    mock_backend = _make_mock_backend()
    mock_backend.upsert.return_value = 1
    router = VectorRouter(backend=mock_backend)

    count = await router.upsert(items)

    mock_backend.upsert.assert_awaited_once_with(items, target=VectorTarget.CORPUS)
    assert count == 1


async def test_upsert_forwards_asset_target_to_backend():
    items = [VectorItem(id="a", vector=[0.1], payload={})]
    mock_backend = _make_mock_backend()
    mock_backend.upsert.return_value = 1
    router = VectorRouter(backend=mock_backend)

    await router.upsert(items, target=VectorTarget.ASSET)

    mock_backend.upsert.assert_awaited_once_with(items, target=VectorTarget.ASSET)


async def test_delete_asset_batch_delegates_to_backend_and_returns_count():
    mock_backend = _make_mock_backend()
    mock_backend.delete_asset_batch.return_value = 7
    router = VectorRouter(backend=mock_backend)

    count = await router.delete_asset_batch("batch-42")

    mock_backend.delete_asset_batch.assert_awaited_once_with("batch-42")
    assert count == 7


async def test_embed_delegates_to_backend_and_returns_embeddings():
    texts = ["hello", "world"]
    expected_embeddings = [[0.1, 0.2], [0.3, 0.4]]
    mock_backend = _make_mock_backend()
    mock_backend.embed.return_value = expected_embeddings
    router = VectorRouter(backend=mock_backend)

    result = await router.embed(texts)

    mock_backend.embed.assert_awaited_once_with(texts)
    assert result == expected_embeddings
