from unittest.mock import AsyncMock

import pytest
from azure.cosmos.exceptions import CosmosHttpResponseError, CosmosResourceNotFoundError

from harvesting.domain.errors.harvesting_errors import StorageWriteError
from harvesting.infrastructure.cosmos.document_read_repository import CosmosDocumentReadRepository

BATCH_ID = "batch-abc"
DOCUMENT_ID = "doc-001"


def _make_repository() -> tuple[CosmosDocumentReadRepository, AsyncMock]:
    container = AsyncMock()
    repo = CosmosDocumentReadRepository(container)
    return repo, container


async def test_get_returns_document_dict_on_success():
    repo, container = _make_repository()
    document = {"id": DOCUMENT_ID, "batch_id": BATCH_ID, "viewable_blob_uri": "https://blob/x.pdf"}
    container.read_item.return_value = document

    result = await repo.get(BATCH_ID, DOCUMENT_ID)

    assert result == document
    container.read_item.assert_awaited_once_with(item=DOCUMENT_ID, partition_key=BATCH_ID)


async def test_get_returns_none_when_document_not_found():
    repo, container = _make_repository()
    container.read_item.side_effect = CosmosResourceNotFoundError()

    result = await repo.get(BATCH_ID, DOCUMENT_ID)

    assert result is None


async def test_get_cosmos_error_raises_storage_write_error():
    repo, container = _make_repository()
    container.read_item.side_effect = CosmosHttpResponseError(
        status_code=500, message="Internal server error"
    )

    with pytest.raises(StorageWriteError):
        await repo.get(BATCH_ID, DOCUMENT_ID)
