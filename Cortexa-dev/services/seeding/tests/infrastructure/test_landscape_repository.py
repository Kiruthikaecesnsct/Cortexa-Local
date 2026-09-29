from datetime import UTC, datetime
from unittest.mock import AsyncMock

import pytest
from azure.cosmos.exceptions import CosmosHttpResponseError

from seeding.domain.errors.seeding_errors import StorageWriteError
from seeding.domain.models.landscape import (
    ConceptLandscape,
    LandscapeSourceFlags,
    PriorArtLandscape,
)
from seeding.infrastructure.cosmos.landscape_repository import (
    LandscapeRepository,
    landscape_id,
)

BATCH_ID = "batch-1"
DOCUMENT_ID = "doc-1"
SCHEMA_VERSION = "1.0"


def _repo() -> tuple[LandscapeRepository, AsyncMock]:
    container = AsyncMock()
    return LandscapeRepository(container), container


def _landscape() -> PriorArtLandscape:
    return PriorArtLandscape(
        id=landscape_id(BATCH_ID, DOCUMENT_ID, SCHEMA_VERSION),
        batch_id=BATCH_ID,
        document_id=DOCUMENT_ID,
        schema_version=SCHEMA_VERSION,
        concepts=[ConceptLandscape(concept="c", chunk_ids=["chunk-0"])],
        source_flags=LandscapeSourceFlags(evidence_reachable=True, corpus_only=False),
        created_at=datetime(2026, 7, 12, tzinfo=UTC),
    )


def test_landscape_id_is_deterministic():
    first = landscape_id(BATCH_ID, DOCUMENT_ID, SCHEMA_VERSION)
    second = landscape_id(BATCH_ID, DOCUMENT_ID, SCHEMA_VERSION)

    assert first == second
    assert first != landscape_id(BATCH_ID, "other-doc", SCHEMA_VERSION)


async def test_save_serializes_datetime_and_sets_partition():
    repo, container = _repo()

    await repo.save_landscape(_landscape())

    document = container.upsert_item.await_args.args[0]
    assert document["batch_id"] == BATCH_ID
    assert isinstance(document["created_at"], str)
    assert "partition_key" not in container.upsert_item.await_args.kwargs


async def test_get_returns_none_when_absent():
    repo, container = _repo()
    container.query_items = lambda **_: _aiter([])

    result = await repo.get_landscape(BATCH_ID, DOCUMENT_ID, SCHEMA_VERSION)

    assert result is None


async def test_get_maps_document():
    repo, container = _repo()
    container.query_items = lambda **_: _aiter([_landscape().model_dump(mode="json")])

    result = await repo.get_landscape(BATCH_ID, DOCUMENT_ID, SCHEMA_VERSION)

    assert result is not None
    assert result.concepts[0].concept == "c"


async def test_save_cosmos_error_raises_storage_write_error():
    repo, container = _repo()
    container.upsert_item.side_effect = CosmosHttpResponseError(status_code=503, message="down")

    with pytest.raises(StorageWriteError):
        await repo.save_landscape(_landscape())


async def _aiter(items):
    for item in items:
        yield item
