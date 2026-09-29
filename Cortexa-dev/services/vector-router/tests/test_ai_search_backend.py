from unittest.mock import AsyncMock, MagicMock, patch

import pytest
from azure.core.exceptions import ResourceNotFoundError

from vector_router.domain.models import SearchFilter, VectorItem, VectorTarget
from vector_router.infrastructure.backends.ai_search_backend import (
    AiSearchBackend,
    AiSearchBackendConfig,
    _build_asset_index,
    _build_corpus_index,
    _build_odata_filter,
)

ENDPOINT = "https://test-search.search.windows.net"
INDEX_NAME = "test-index"
ASSET_INDEX_NAME = "test-asset-index"
DIMENSIONS = 3


def _make_config() -> AiSearchBackendConfig:
    return AiSearchBackendConfig(
        endpoint=ENDPOINT,
        index_name=INDEX_NAME,
        asset_index_name=ASSET_INDEX_NAME,
        dimensions=DIMENSIONS,
    )


def _make_backend(mock_credential, mock_embedder) -> AiSearchBackend:
    return AiSearchBackend(
        config=_make_config(),
        credential=mock_credential,
        embedder=mock_embedder,
    )


def _make_search_result(doc_id: str, score: float, payload: str | dict | None) -> dict:
    return {"id": doc_id, "@search.score": score, "payload": payload}


def _make_async_iter(items: list):
    async def _gen():
        for item in items:
            yield item

    return _gen()


def _build_mock_client(search_results: list, upload_results: list | None = None):
    mock_client = AsyncMock()
    mock_client.__aenter__ = AsyncMock(return_value=mock_client)
    mock_client.__aexit__ = AsyncMock(return_value=False)
    mock_client.search = AsyncMock(return_value=_make_async_iter(search_results))
    if upload_results is not None:
        mock_client.merge_or_upload_documents = AsyncMock(return_value=upload_results)
    return mock_client


async def test_search_no_filters_returns_vector_hits(mock_credential, mock_embedder):
    raw_results = [_make_search_result("doc-1", 0.9, '{"title": "Paper One"}')]
    mock_client = _build_mock_client(raw_results)
    backend = _make_backend(mock_credential, mock_embedder)

    with patch.object(backend, "_client", return_value=mock_client):
        hits = await backend.search([0.1, 0.2, 0.3], top_k=5, filters=None)

    assert len(hits) == 1
    assert hits[0].id == "doc-1"
    assert hits[0].score == pytest.approx(0.9)
    assert hits[0].payload == {"title": "Paper One"}


async def test_search_calls_sdk_with_correct_vector_query(mock_credential, mock_embedder):
    embedding = [0.1, 0.2, 0.3]
    mock_client = _build_mock_client([])
    backend = _make_backend(mock_credential, mock_embedder)

    with patch.object(backend, "_client", return_value=mock_client):
        await backend.search(embedding, top_k=10, filters=None)

    call_kwargs = mock_client.search.call_args.kwargs
    assert call_kwargs["search_text"] is None
    assert call_kwargs["vector_queries"][0]["vector"] == embedding
    assert call_kwargs["vector_queries"][0]["k"] == 10
    assert call_kwargs["filter"] is None


async def test_search_score_at_upper_bound_passes_through(mock_credential, mock_embedder):
    raw_results = [_make_search_result("doc-1", 1.0, {})]
    mock_client = _build_mock_client(raw_results)
    backend = _make_backend(mock_credential, mock_embedder)

    with patch.object(backend, "_client", return_value=mock_client):
        hits = await backend.search([0.1], top_k=1, filters=None)

    assert hits[0].score == pytest.approx(1.0)


async def test_search_score_below_lower_bound_is_clamped_to_zero(mock_credential, mock_embedder):
    raw_results = [_make_search_result("doc-1", -1.0, {})]
    mock_client = _build_mock_client(raw_results)
    backend = _make_backend(mock_credential, mock_embedder)

    with patch.object(backend, "_client", return_value=mock_client):
        hits = await backend.search([0.1], top_k=1, filters=None)

    assert hits[0].score == pytest.approx(0.0)


async def test_search_score_zero_stays_zero(mock_credential, mock_embedder):
    raw_results = [_make_search_result("doc-1", 0.0, {})]
    mock_client = _build_mock_client(raw_results)
    backend = _make_backend(mock_credential, mock_embedder)

    with patch.object(backend, "_client", return_value=mock_client):
        hits = await backend.search([0.1], top_k=1, filters=None)

    assert hits[0].score == pytest.approx(0.0)


async def test_search_mid_range_score_passes_through_unchanged(mock_credential, mock_embedder):
    raw_results = [_make_search_result("doc-1", 0.66, {})]
    mock_client = _build_mock_client(raw_results)
    backend = _make_backend(mock_credential, mock_embedder)

    with patch.object(backend, "_client", return_value=mock_client):
        hits = await backend.search([0.1], top_k=1, filters=None)

    assert hits[0].score == pytest.approx(0.66)
    assert hits[0].score != pytest.approx(0.83)


async def test_search_with_filters_builds_odata_filter_string(mock_credential, mock_embedder):
    mock_client = _build_mock_client([])
    backend = _make_backend(mock_credential, mock_embedder)
    filters = [SearchFilter(field="batch_id", value="batch-42")]

    with patch.object(backend, "_client", return_value=mock_client):
        await backend.search([0.1], top_k=5, filters=filters)

    call_kwargs = mock_client.search.call_args.kwargs
    assert call_kwargs["filter"] == "batch_id eq 'batch-42'"


async def test_upsert_forwards_documents_to_sdk_and_returns_success_count(
    mock_credential, mock_embedder
):
    succeeded_result = MagicMock(succeeded=True)
    failed_result = MagicMock(succeeded=False)
    mock_client = _build_mock_client([], upload_results=[succeeded_result, failed_result])
    backend = _make_backend(mock_credential, mock_embedder)
    items = [
        VectorItem(id="a", vector=[0.1, 0.2], payload={"k": "v"}),
        VectorItem(id="b", vector=[0.3, 0.4], payload={"k": "w"}),
    ]

    with patch.object(backend, "_client", return_value=mock_client):
        count = await backend.upsert(items)

    assert count == 1
    uploaded_docs = mock_client.merge_or_upload_documents.call_args.kwargs["documents"]
    assert len(uploaded_docs) == 2
    assert uploaded_docs[0]["id"] == "a"
    assert uploaded_docs[1]["id"] == "b"


async def test_embed_delegates_to_embedder(mock_credential, mock_embedder):
    backend = _make_backend(mock_credential, mock_embedder)

    result = await backend.embed(["hello world"])

    mock_embedder.embed.assert_awaited_once_with(["hello world"])
    assert result == [[0.1, 0.1, 0.1]]


async def test_payload_serialized_on_upsert(mock_credential, mock_embedder):
    succeeded_result = MagicMock(succeeded=True)
    mock_client = _build_mock_client([], upload_results=[succeeded_result])
    backend = _make_backend(mock_credential, mock_embedder)
    items = [VectorItem(id="x", vector=[0.1], payload={"key": "value", "num": 42})]

    with patch.object(backend, "_client", return_value=mock_client):
        await backend.upsert(items)

    uploaded_docs = mock_client.merge_or_upload_documents.call_args.kwargs["documents"]
    assert uploaded_docs[0]["payload"] == '{"key": "value", "num": 42}'


async def test_payload_deserialized_on_search(mock_credential, mock_embedder):
    raw_results = [_make_search_result("doc-1", 0.0, '{"key": "val", "num": 42}')]
    mock_client = _build_mock_client(raw_results)
    backend = _make_backend(mock_credential, mock_embedder)

    with patch.object(backend, "_client", return_value=mock_client):
        hits = await backend.search([0.1], top_k=1, filters=None)

    assert hits[0].payload == {"key": "val", "num": 42}


async def test_payload_empty_when_none_on_search(mock_credential, mock_embedder):
    raw_results = [_make_search_result("doc-1", 0.0, None)]
    mock_client = _build_mock_client(raw_results)
    backend = _make_backend(mock_credential, mock_embedder)

    with patch.object(backend, "_client", return_value=mock_client):
        hits = await backend.search([0.1], top_k=1, filters=None)

    assert hits[0].payload == {}


async def test_payload_empty_when_invalid_json_on_search(mock_credential, mock_embedder):
    raw_results = [_make_search_result("doc-1", 0.0, "not-json")]
    mock_client = _build_mock_client(raw_results)
    backend = _make_backend(mock_credential, mock_embedder)

    with patch.object(backend, "_client", return_value=mock_client):
        hits = await backend.search([0.1], top_k=1, filters=None)

    assert hits[0].payload == {}


async def test_ensure_index_creates_index_when_missing(mock_credential, mock_embedder):
    backend = _make_backend(mock_credential, mock_embedder)
    mock_index_client = AsyncMock()
    mock_index_client.__aenter__ = AsyncMock(return_value=mock_index_client)
    mock_index_client.__aexit__ = AsyncMock(return_value=False)
    mock_index_client.get_index = AsyncMock(side_effect=ResourceNotFoundError)
    mock_index_client.create_index = AsyncMock()

    with patch(
        "vector_router.infrastructure.backends.ai_search_backend.SearchIndexClient",
        return_value=mock_index_client,
    ):
        await backend.ensure_index()

    mock_index_client.get_index.assert_awaited_once_with(INDEX_NAME)
    mock_index_client.create_index.assert_awaited_once()
    created_index = mock_index_client.create_index.call_args.args[0]
    assert created_index.name == INDEX_NAME


async def test_ensure_index_skips_when_index_exists(mock_credential, mock_embedder):
    backend = _make_backend(mock_credential, mock_embedder)
    mock_index_client = AsyncMock()
    mock_index_client.__aenter__ = AsyncMock(return_value=mock_index_client)
    mock_index_client.__aexit__ = AsyncMock(return_value=False)
    mock_index_client.get_index = AsyncMock(return_value=MagicMock())
    mock_index_client.create_index = AsyncMock()

    with patch(
        "vector_router.infrastructure.backends.ai_search_backend.SearchIndexClient",
        return_value=mock_index_client,
    ):
        await backend.ensure_index()

    mock_index_client.get_index.assert_awaited_once_with(INDEX_NAME)
    mock_index_client.create_index.assert_not_awaited()


async def test_search_asset_target_uses_asset_client(mock_credential, mock_embedder):
    mock_client = _build_mock_client([])
    backend = _make_backend(mock_credential, mock_embedder)

    with patch.object(backend, "_asset_client", return_value=mock_client) as asset_client:
        await backend.search([0.1], top_k=5, filters=None, target=VectorTarget.ASSET)

    asset_client.assert_called_once()


async def test_upsert_asset_target_uses_asset_client(mock_credential, mock_embedder):
    succeeded_result = MagicMock(succeeded=True)
    mock_client = _build_mock_client([], upload_results=[succeeded_result])
    backend = _make_backend(mock_credential, mock_embedder)
    items = [VectorItem(id="x", vector=[0.1], payload={"batch_id": "b1"})]

    with patch.object(backend, "_asset_client", return_value=mock_client) as asset_client:
        count = await backend.upsert(items, target=VectorTarget.ASSET)

    assert count == 1
    asset_client.assert_called_once()


async def test_search_asset_target_allows_document_id_filter(mock_credential, mock_embedder):
    mock_client = _build_mock_client([])
    backend = _make_backend(mock_credential, mock_embedder)
    filters = [SearchFilter(field="document_id", value="doc-1")]

    with patch.object(backend, "_asset_client", return_value=mock_client):
        await backend.search([0.1], top_k=5, filters=filters, target=VectorTarget.ASSET)

    assert mock_client.search.call_args.kwargs["filter"] == "document_id eq 'doc-1'"


async def test_search_corpus_target_rejects_document_id_filter(mock_credential, mock_embedder):
    mock_client = _build_mock_client([])
    backend = _make_backend(mock_credential, mock_embedder)
    filters = [SearchFilter(field="document_id", value="doc-1")]

    with patch.object(backend, "_client", return_value=mock_client):
        with pytest.raises(ValueError, match="Unknown filter field"):
            await backend.search([0.1], top_k=5, filters=filters)


async def test_delete_asset_batch_pages_and_returns_total(mock_credential, mock_embedder):
    backend = _make_backend(mock_credential, mock_embedder)
    mock_client = AsyncMock()
    mock_client.__aenter__ = AsyncMock(return_value=mock_client)
    mock_client.__aexit__ = AsyncMock(return_value=False)
    first_page = _make_async_iter([{"id": "a"}, {"id": "b"}])
    empty_page = _make_async_iter([])
    mock_client.search = AsyncMock(side_effect=[first_page, empty_page])
    mock_client.delete_documents = AsyncMock()

    with patch.object(backend, "_asset_client", return_value=mock_client):
        deleted = await backend.delete_asset_batch("batch-42")

    assert deleted == 2
    assert mock_client.search.call_args_list[0].kwargs["filter"] == "batch_id eq 'batch-42'"
    deleted_docs = mock_client.delete_documents.call_args.kwargs["documents"]
    assert deleted_docs == [{"id": "a"}, {"id": "b"}]


async def test_delete_asset_batch_empty_returns_zero(mock_credential, mock_embedder):
    backend = _make_backend(mock_credential, mock_embedder)
    mock_client = AsyncMock()
    mock_client.__aenter__ = AsyncMock(return_value=mock_client)
    mock_client.__aexit__ = AsyncMock(return_value=False)
    mock_client.search = AsyncMock(return_value=_make_async_iter([]))
    mock_client.delete_documents = AsyncMock()

    with patch.object(backend, "_asset_client", return_value=mock_client):
        deleted = await backend.delete_asset_batch("missing")

    assert deleted == 0
    mock_client.delete_documents.assert_not_awaited()


async def test_ensure_asset_store_creates_asset_index_when_missing(mock_credential, mock_embedder):
    backend = _make_backend(mock_credential, mock_embedder)
    mock_index_client = AsyncMock()
    mock_index_client.__aenter__ = AsyncMock(return_value=mock_index_client)
    mock_index_client.__aexit__ = AsyncMock(return_value=False)
    mock_index_client.get_index = AsyncMock(side_effect=ResourceNotFoundError)
    mock_index_client.create_index = AsyncMock()

    with patch(
        "vector_router.infrastructure.backends.ai_search_backend.SearchIndexClient",
        return_value=mock_index_client,
    ):
        await backend.ensure_asset_store()

    mock_index_client.get_index.assert_awaited_once_with(ASSET_INDEX_NAME)
    created_index = mock_index_client.create_index.call_args.args[0]
    assert created_index.name == ASSET_INDEX_NAME


def test_build_asset_index_has_correct_schema():
    index = _build_asset_index("asset-idx", 1536)

    assert index.name == "asset-idx"
    fields = {f.name: f for f in index.fields}
    assert fields["id"].key is True
    assert fields["content_vector"].vector_search_dimensions == 1536
    assert "payload" in fields
    for field_name in ["batch_id", "document_id", "chunk_id"]:
        assert field_name in fields
        assert fields[field_name].filterable is True


def test_asset_allow_list_permits_asset_fields():
    for field in ["batch_id", "document_id", "chunk_id"]:
        filters = [SearchFilter(field=field, value="v")]
        result = _build_odata_filter(filters, frozenset({"batch_id", "document_id", "chunk_id"}))
        assert result == f"{field} eq 'v'"


def test_build_corpus_index_has_correct_schema():
    index = _build_corpus_index("test-idx", 768)

    assert index.name == "test-idx"

    fields = {f.name: f for f in index.fields}
    assert "id" in fields
    assert fields["id"].key is True
    assert fields["id"].filterable is True

    assert "content_vector" in fields
    assert fields["content_vector"].searchable is True
    assert fields["content_vector"].vector_search_dimensions == 768
    assert fields["content_vector"].vector_search_profile_name == "cortexa-hnsw"

    assert "payload" in fields
    for field_name in ["source", "doc_type", "batch_id", "chunk_index"]:
        assert field_name in fields
        assert fields[field_name].filterable is True

    assert index.vector_search is not None
    assert len(index.vector_search.profiles) == 1
    assert index.vector_search.profiles[0].name == "cortexa-hnsw"
    assert len(index.vector_search.algorithms) == 1
    assert index.vector_search.algorithms[0].name == "cortexa-hnsw-algo"
