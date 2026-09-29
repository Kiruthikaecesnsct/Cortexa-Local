from unittest.mock import AsyncMock

import pytest
from azure.cosmos.exceptions import CosmosHttpResponseError, CosmosResourceNotFoundError

from harvesting.domain.errors.harvesting_errors import StorageWriteError
from harvesting.infrastructure.cosmos.provenance_entry_read_repository import (
    CosmosProvenanceEntryReadRepository,
)

BATCH_ID = "batch-abc"
CHUNK_ID = "repo-1|abcd1234|4"


def _make_repository() -> tuple[CosmosProvenanceEntryReadRepository, AsyncMock]:
    container = AsyncMock()
    repo = CosmosProvenanceEntryReadRepository(container)
    return repo, container


async def test_get_returns_provenance_entry_dict_on_success():
    repo, container = _make_repository()
    entry = {
        "chunk_id": CHUNK_ID,
        "source_kind": "code",
        "order_index": 4,
        "doc_id": "repo-1",
        "file_path": "src/train.py",
        "line_range": [12, 20],
    }
    container.read_item.return_value = entry

    result = await repo.get(BATCH_ID, CHUNK_ID)

    assert result == entry
    container.read_item.assert_awaited_once_with(item=CHUNK_ID, partition_key=BATCH_ID)


async def test_get_returns_none_when_entry_not_found():
    repo, container = _make_repository()
    container.read_item.side_effect = CosmosResourceNotFoundError()

    result = await repo.get(BATCH_ID, CHUNK_ID)

    assert result is None


async def test_get_cosmos_error_raises_storage_write_error():
    repo, container = _make_repository()
    container.read_item.side_effect = CosmosHttpResponseError(
        status_code=500, message="Internal server error"
    )

    with pytest.raises(StorageWriteError):
        await repo.get(BATCH_ID, CHUNK_ID)
