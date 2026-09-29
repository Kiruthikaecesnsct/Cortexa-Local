from unittest.mock import AsyncMock

import pytest
from azure.cosmos.exceptions import CosmosHttpResponseError

from harvesting.domain.errors.harvesting_errors import StorageWriteError
from harvesting.infrastructure.cosmos.chunk_read_repository import CosmosChunkReadRepository

BATCH_ID = "batch-abc"
DOCUMENT_ID = "doc-001"


def _make_repository() -> tuple[CosmosChunkReadRepository, AsyncMock]:
    container = AsyncMock()
    repo = CosmosChunkReadRepository(container)
    return repo, container


async def test_get_by_document_order_returns_matching_chunk_dict():
    repo, container = _make_repository()
    chunk = {"id": f"{DOCUMENT_ID}|2", "document_id": DOCUMENT_ID, "order_index": 2, "text": "hi"}

    async def mock_query_items(query, parameters, partition_key):
        yield chunk

    container.query_items = mock_query_items

    result = await repo.get_by_document_order(BATCH_ID, DOCUMENT_ID, 2)

    assert result == chunk


async def test_get_by_document_order_returns_none_when_no_match():
    repo, container = _make_repository()

    async def mock_query_items(query, parameters, partition_key):
        return
        yield

    container.query_items = mock_query_items

    result = await repo.get_by_document_order(BATCH_ID, DOCUMENT_ID, 99)

    assert result is None


async def test_get_by_document_order_cosmos_error_raises_storage_write_error():
    repo, container = _make_repository()

    async def mock_query_items(query, parameters, partition_key):
        raise CosmosHttpResponseError(status_code=500, message="Internal server error")
        yield

    container.query_items = mock_query_items

    with pytest.raises(StorageWriteError):
        await repo.get_by_document_order(BATCH_ID, DOCUMENT_ID, 2)


async def test_get_by_document_order_queries_by_document_and_order_index():
    repo, container = _make_repository()
    captured: dict = {}

    async def mock_query_items(query, parameters, partition_key):
        captured["query"] = query
        captured["parameters"] = parameters
        captured["partition_key"] = partition_key
        return
        yield

    container.query_items = mock_query_items

    await repo.get_by_document_order(BATCH_ID, DOCUMENT_ID, 7)

    assert "c.document_id = @document_id" in captured["query"]
    assert "c.order_index = @order_index" in captured["query"]
    assert captured["partition_key"] == BATCH_ID
    param_values = {p["name"]: p["value"] for p in captured["parameters"]}
    assert param_values["@document_id"] == DOCUMENT_ID
    assert param_values["@order_index"] == 7
