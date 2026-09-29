from unittest.mock import AsyncMock

import pytest
from azure.cosmos.exceptions import CosmosHttpResponseError

from seeding.domain.errors.seeding_errors import StorageWriteError
from seeding.domain.models.seeding_result import SeedingResult
from seeding.infrastructure.cosmos.seeding_report_repository import SeedingReportRepository

BATCH_ID = "batch-abc"


def _make_result() -> SeedingResult:
    return SeedingResult(id="seed-001", batch_id=BATCH_ID, opportunities=[])


def _make_repository() -> tuple[SeedingReportRepository, AsyncMock]:
    container = AsyncMock()
    return SeedingReportRepository(container), container


async def test_save_upserts_without_partition_key_kwarg():
    repo, container = _make_repository()

    await repo.save(_make_result())

    container.upsert_item.assert_called_once()
    call_args = container.upsert_item.call_args
    document = call_args.args[0]
    # batch_id lives in the document body; the aio upsert_item derives the
    # partition key from it and rejects a partition_key kwarg (BUG105).
    assert document["batch_id"] == BATCH_ID
    assert "partition_key" not in call_args.kwargs


async def test_save_cosmos_error_raises_storage_write_error():
    repo, container = _make_repository()
    container.upsert_item.side_effect = CosmosHttpResponseError(
        status_code=503, message="Service unavailable"
    )

    with pytest.raises(StorageWriteError):
        await repo.save(_make_result())
