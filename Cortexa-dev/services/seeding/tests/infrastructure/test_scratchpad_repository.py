from datetime import UTC, datetime
from unittest.mock import AsyncMock

import pytest
from azure.cosmos.exceptions import CosmosHttpResponseError

from seeding.domain.errors.seeding_errors import StorageWriteError
from seeding.domain.models.ideation import RoundRecord
from seeding.domain.models.scratchpad import AcceptedIdea, IdeationScratchpad
from seeding.infrastructure.cosmos.scratchpad_repository import (
    ScratchpadRepository,
    scratchpad_id,
)

BATCH_ID = "batch-1"
DOCUMENT_ID = "doc-1"
SCHEMA_VERSION = "1.0"


def _repo() -> tuple[ScratchpadRepository, AsyncMock]:
    container = AsyncMock()
    return ScratchpadRepository(container), container


def _scratchpad() -> IdeationScratchpad:
    return IdeationScratchpad(
        id=scratchpad_id(BATCH_ID, DOCUMENT_ID, SCHEMA_VERSION),
        batch_id=BATCH_ID,
        document_id=DOCUMENT_ID,
        schema_version=SCHEMA_VERSION,
        prompt_version="1.0.0",
        rounds_completed=2,
        accepted_ideas=[AcceptedIdea(title="idea", chunk_ids=["chunk-1"], round_index=1)],
        rounds=[RoundRecord(round=0, accepted_count=1), RoundRecord(round=1, accepted_count=0)],
        status="in_progress",
        created_at=datetime(2026, 7, 12, tzinfo=UTC),
        updated_at=datetime(2026, 7, 12, tzinfo=UTC),
    )


def test_scratchpad_id_is_deterministic():
    first = scratchpad_id(BATCH_ID, DOCUMENT_ID, SCHEMA_VERSION)
    second = scratchpad_id(BATCH_ID, DOCUMENT_ID, SCHEMA_VERSION)

    assert first == second
    assert first != scratchpad_id(BATCH_ID, "other-doc", SCHEMA_VERSION)
    assert first != scratchpad_id(BATCH_ID, DOCUMENT_ID, "2.0")


async def test_save_serializes_datetime_and_sets_batch_id_partition():
    repo, container = _repo()

    await repo.save(_scratchpad())

    document = container.upsert_item.await_args.args[0]
    assert document["batch_id"] == BATCH_ID
    assert document["id"] == scratchpad_id(BATCH_ID, DOCUMENT_ID, SCHEMA_VERSION)
    assert isinstance(document["created_at"], str)
    assert "partition_key" not in container.upsert_item.await_args.kwargs


async def test_get_returns_none_when_absent():
    repo, container = _repo()
    container.query_items = lambda **_: _aiter([])

    result = await repo.get(BATCH_ID, DOCUMENT_ID, SCHEMA_VERSION)

    assert result is None


async def test_get_maps_document_round_trip():
    repo, container = _repo()
    original = _scratchpad()
    container.query_items = lambda **_: _aiter([original.model_dump(mode="json")])

    result = await repo.get(BATCH_ID, DOCUMENT_ID, SCHEMA_VERSION)

    assert result is not None
    assert result.id == original.id
    assert result.rounds_completed == 2
    assert result.accepted_ideas[0].title == "idea"
    assert len(result.rounds) == 2


async def test_save_cosmos_error_raises_storage_write_error():
    repo, container = _repo()
    container.upsert_item.side_effect = CosmosHttpResponseError(status_code=503, message="down")

    with pytest.raises(StorageWriteError):
        await repo.save(_scratchpad())


async def test_get_cosmos_error_raises_storage_write_error():
    repo, container = _repo()

    def _raise(**_):
        raise CosmosHttpResponseError(status_code=503, message="down")

    container.query_items = _raise

    with pytest.raises(StorageWriteError):
        await repo.get(BATCH_ID, DOCUMENT_ID, SCHEMA_VERSION)


async def _aiter(items):
    for item in items:
        yield item
