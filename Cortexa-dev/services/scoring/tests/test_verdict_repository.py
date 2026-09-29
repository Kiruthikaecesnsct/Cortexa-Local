from unittest.mock import AsyncMock

import pytest
from azure.cosmos.exceptions import CosmosHttpResponseError, CosmosResourceNotFoundError

from scoring.domain.enums.agreement_level import AgreementLevel
from scoring.domain.enums.scoring_axis import ScoringAxis
from scoring.domain.errors.storage_errors import StorageWriteError
from scoring.domain.models.axis_score import AxisScore
from scoring.domain.models.stored_verdict import StoredVerdict
from scoring.infrastructure.cosmos.verdict_repository import CosmosVerdictRepository

BATCH_ID = "batch-abc"
CANDIDATE_ID = "cand-001"


def _make_verdict() -> StoredVerdict:
    axes = {axis: AxisScore(axis=axis, score=50, refs=[]) for axis in ScoringAxis}
    return StoredVerdict(
        id=f"{BATCH_ID}:{CANDIDATE_ID}",
        batch_id=BATCH_ID,
        job_id="job-1",
        candidate_id=CANDIDATE_ID,
        document_id="doc-xyz",
        axes=axes,
        composite_score=50.0,
        agreement_level=AgreementLevel.Full,
        agreeing_axis_count=5,
    )


def _make_repository() -> tuple[CosmosVerdictRepository, AsyncMock]:
    container = AsyncMock()
    return CosmosVerdictRepository(container), container


async def test_save_upserts_without_partition_key_kwarg():
    repo, container = _make_repository()

    await repo.save(_make_verdict())

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
        await repo.save(_make_verdict())


async def test_find_by_candidate_reads_with_partition_key():
    repo, container = _make_repository()
    container.read_item.return_value = _make_verdict().model_dump(mode="json")

    result = await repo.find_by_candidate(CANDIDATE_ID, BATCH_ID)

    assert result is not None
    assert container.read_item.call_args.kwargs["partition_key"] == BATCH_ID


async def test_find_by_candidate_not_found_returns_none():
    repo, container = _make_repository()
    container.read_item.side_effect = CosmosResourceNotFoundError(
        status_code=404, message="not found"
    )

    assert await repo.find_by_candidate(CANDIDATE_ID, BATCH_ID) is None
