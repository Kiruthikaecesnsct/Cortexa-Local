from unittest.mock import AsyncMock

import pytest
from azure.cosmos.exceptions import CosmosHttpResponseError

from harvesting.domain.errors.harvesting_errors import StorageWriteError
from harvesting.infrastructure.cosmos.candidate_read_repository import CosmosCandidateReadRepository

BATCH_ID = "batch-abc"
CANDIDATE_ID = "cand-001"


def _make_candidate_dict() -> dict:
    return {
        "id": "candidate-001",
        "candidate_id": CANDIDATE_ID,
        "batch_id": BATCH_ID,
        "title": "Test Invention",
        "description": "A novel compression method.",
    }


def _make_repository() -> tuple[CosmosCandidateReadRepository, AsyncMock]:
    container = AsyncMock()
    repo = CosmosCandidateReadRepository(container)
    return repo, container


async def test_get_by_batch_returns_list_of_candidate_dicts():
    repo, container = _make_repository()
    candidate_dict = _make_candidate_dict()

    async def mock_query_items(query, parameters, partition_key):
        yield candidate_dict

    container.query_items = mock_query_items

    result = await repo.get_by_batch(BATCH_ID)

    assert isinstance(result, list)
    assert len(result) == 1
    assert result[0]["id"] == "candidate-001"
    assert result[0]["candidate_id"] == CANDIDATE_ID


async def test_get_by_batch_returns_empty_list_when_no_results():
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


async def test_get_by_batch_query_excludes_seeding_engine():
    repo, container = _make_repository()
    captured: dict = {}

    async def mock_query_items(query, parameters, partition_key):
        captured["query"] = query
        return
        yield

    container.query_items = mock_query_items

    await repo.get_by_batch(BATCH_ID)

    query = captured["query"]
    assert "NOT IS_DEFINED(c.engine)" in query
    assert "c.engine != 'seeding'" in query


async def test_get_by_batch_includes_candidate_without_engine_field():
    repo, container = _make_repository()
    harvesting_candidate = _make_candidate_dict()

    async def mock_query_items(query, parameters, partition_key):
        yield harvesting_candidate

    container.query_items = mock_query_items

    result = await repo.get_by_batch(BATCH_ID)

    assert len(result) == 1
    assert "engine" not in result[0]
    assert result[0]["candidate_id"] == CANDIDATE_ID


async def test_get_by_batch_multiple_candidates_returns_all():
    repo, container = _make_repository()
    candidate_1 = _make_candidate_dict()
    candidate_2 = {
        "id": "candidate-002",
        "candidate_id": "cand-002",
        "batch_id": BATCH_ID,
        "title": "Another Invention",
        "description": "A second novel method.",
    }

    async def mock_query_items(query, parameters, partition_key):
        yield candidate_1
        yield candidate_2

    container.query_items = mock_query_items

    result = await repo.get_by_batch(BATCH_ID)

    assert len(result) == 2
    assert result[0]["id"] == "candidate-001"
    assert result[1]["id"] == "candidate-002"
