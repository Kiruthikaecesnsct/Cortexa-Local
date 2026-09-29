from datetime import UTC, datetime
from unittest.mock import AsyncMock

import pytest
from azure.cosmos.exceptions import CosmosHttpResponseError

from seeding.domain.errors.seeding_errors import StorageWriteError
from seeding.domain.models.digest import CitedEntry, InventionContextBrief, SectionNote
from seeding.infrastructure.cosmos.digest_repository import DigestRepository

BATCH_ID = "batch-1"
DOCUMENT_ID = "doc-1"


def _repo() -> tuple[DigestRepository, AsyncMock]:
    container = AsyncMock()
    return DigestRepository(container), container


def _note() -> SectionNote:
    return SectionNote(
        id="note-1",
        batch_id=BATCH_ID,
        document_id=DOCUMENT_ID,
        section_key="note-1",
        section_label="Intro",
        chunk_ids=["c1"],
        claims_made=[CitedEntry(text="x", chunk_ids=["c1"])],
        schema_version="1.0",
        prompt_version="1.0.0",
        created_at=datetime(2026, 7, 12, tzinfo=UTC),
    )


def _brief() -> InventionContextBrief:
    return InventionContextBrief(
        id="brief-1",
        batch_id=BATCH_ID,
        document_id=DOCUMENT_ID,
        schema_version="1.0",
        prompt_version="1.0.0",
        created_at=datetime(2026, 7, 12, tzinfo=UTC),
    )


async def test_save_section_note_serializes_datetime_as_json():
    repo, container = _repo()

    await repo.save_section_note(_note())

    document = container.upsert_item.await_args.args[0]
    assert document["batch_id"] == BATCH_ID
    assert isinstance(document["created_at"], str)
    assert "partition_key" not in container.upsert_item.await_args.kwargs


async def test_save_brief_serializes_datetime_as_json():
    repo, container = _repo()

    await repo.save_brief(_brief())

    document = container.upsert_item.await_args.args[0]
    assert isinstance(document["created_at"], str)
    assert document["id"] == "brief-1"


async def test_get_brief_returns_none_when_absent():
    repo, container = _repo()
    container.query_items = lambda **_: _aiter([])

    result = await repo.get_brief(BATCH_ID, DOCUMENT_ID, "1.0.0")

    assert result is None


async def test_get_section_notes_maps_documents():
    repo, container = _repo()
    container.query_items = lambda **_: _aiter([_note().model_dump(mode="json")])

    notes = await repo.get_section_notes(BATCH_ID, DOCUMENT_ID, "1.0.0")

    assert len(notes) == 1
    assert notes[0].section_key == "note-1"


async def test_save_cosmos_error_raises_storage_write_error():
    repo, container = _repo()
    container.upsert_item.side_effect = CosmosHttpResponseError(status_code=503, message="down")

    with pytest.raises(StorageWriteError):
        await repo.save_brief(_brief())


async def _aiter(items):
    for item in items:
        yield item
