from unittest.mock import AsyncMock

import pytest
from azure.cosmos.exceptions import CosmosHttpResponseError

from harvesting.domain.enums.maturity import Maturity
from harvesting.domain.errors.harvesting_errors import StorageWriteError
from harvesting.domain.models.maturity_result import MaturityResult
from harvesting.infrastructure.cosmos.maturity_repository import CosmosMaturityRepository

BATCH_ID = "batch-abc"
CANDIDATE_ID = "cand-001"


def _make_result() -> MaturityResult:
    return MaturityResult(
        id=f"{BATCH_ID}:{CANDIDATE_ID}",
        candidate_id=CANDIDATE_ID,
        batch_id=BATCH_ID,
        document_id="doc-xyz",
        maturity=Maturity.Emerging,
        novelty_score=60,
        feasibility_score=70,
        reasoning="novelty=60, feasibility=70",
    )


def _make_repository() -> tuple[CosmosMaturityRepository, AsyncMock]:
    container = AsyncMock()
    return CosmosMaturityRepository(container), container


async def test_save_upserts_without_partition_key_kwarg():
    repo, container = _make_repository()

    await repo.save(_make_result())

    container.upsert_item.assert_called_once()
    call_args = container.upsert_item.call_args
    item_dict = call_args.args[0]
    # batch_id lives in the document body; the aio upsert_item derives the
    # partition key from it and rejects a partition_key kwarg (BUG105).
    assert item_dict["batch_id"] == BATCH_ID
    assert "partition_key" not in call_args.kwargs


async def test_save_cosmos_error_raises_storage_write_error():
    repo, container = _make_repository()
    container.upsert_item.side_effect = CosmosHttpResponseError(
        status_code=503, message="Service unavailable"
    )

    with pytest.raises(StorageWriteError):
        await repo.save(_make_result())
