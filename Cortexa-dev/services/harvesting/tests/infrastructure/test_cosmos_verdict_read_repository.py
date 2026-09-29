from unittest.mock import AsyncMock

import pytest
from azure.cosmos.exceptions import CosmosHttpResponseError

from harvesting.domain.errors.harvesting_errors import StorageWriteError
from harvesting.infrastructure.cosmos.verdict_read_repository import CosmosVerdictReadRepository

BATCH_ID = "batch-abc"
CANDIDATE_ID = "cand-001"
COMPOSITE_SCORE = 85.0


def _make_verdict_dict() -> dict:
    return {
        "id": "verdict-001",
        "candidate_id": CANDIDATE_ID,
        "composite_score": COMPOSITE_SCORE,
        "batch_id": BATCH_ID,
        "axes": {
            "Novelty": {"score": 80},
            "Patentability": {"score": 90},
        },
    }


def _make_repository() -> tuple[CosmosVerdictReadRepository, AsyncMock]:
    container = AsyncMock()
    repo = CosmosVerdictReadRepository(container)
    return repo, container


async def test_get_by_batch_returns_list_of_verdict_dicts():
    repo, container = _make_repository()
    verdict_dict = _make_verdict_dict()

    async def mock_query_items(query, parameters, partition_key):
        yield verdict_dict

    container.query_items = mock_query_items

    result = await repo.get_by_batch(BATCH_ID)

    assert isinstance(result, list)
    assert len(result) == 1
    assert result[0]["id"] == "verdict-001"
    assert result[0]["candidate_id"] == CANDIDATE_ID


async def test_get_by_batch_returns_empty_list_when_no_verdicts_found():
    repo, container = _make_repository()

    async def mock_query_items(query, parameters, partition_key):
        return
        yield

    container.query_items = mock_query_items

    result = await repo.get_by_batch(BATCH_ID)

    assert isinstance(result, list)
    assert len(result) == 0


async def test_get_by_batch_cosmos_error_raises_storage_write_error():
    repo, container = _make_repository()

    async def mock_query_items(query, parameters, partition_key):
        raise CosmosHttpResponseError(status_code=500, message="Internal server error")
        yield

    container.query_items = mock_query_items

    with pytest.raises(StorageWriteError):
        await repo.get_by_batch(BATCH_ID)


async def test_get_by_batch_multiple_verdicts_returns_all():
    repo, container = _make_repository()
    verdict_1 = _make_verdict_dict()
    verdict_2 = {
        "id": "verdict-002",
        "candidate_id": "cand-002",
        "composite_score": 60.0,
        "batch_id": BATCH_ID,
    }

    async def mock_query_items(query, parameters, partition_key):
        yield verdict_1
        yield verdict_2

    container.query_items = mock_query_items

    result = await repo.get_by_batch(BATCH_ID)

    assert len(result) == 2
    assert result[0]["id"] == "verdict-001"
    assert result[1]["id"] == "verdict-002"
