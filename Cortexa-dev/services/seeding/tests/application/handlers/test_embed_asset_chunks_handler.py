from unittest.mock import AsyncMock
from uuid import uuid5

import pytest

from seeding.application.handlers.embed_asset_chunks_handler import (
    ASSET_NS,
    EmbedAssetChunksDeps,
    EmbedAssetChunksHandler,
)
from seeding.application.handlers.process_seeding_request_handler import ProcessOutcome
from seeding.domain.errors.seeding_errors import (
    VectorRouterPermanentError,
    VectorRouterTransientError,
)
from seeding.domain.events.asset_embedding import (
    AssetEmbeddingEnvelope,
    AssetEmbeddingPayload,
)
from seeding.infrastructure.config.settings import SeedingSettings

BATCH_ID = "batch-001"
DOCUMENT_ID = "doc-001"
CORRELATION_ID = "corr-001"
COMPLETED_TOPIC = "asset-embedding-completed"


def _settings() -> SeedingSettings:
    return SeedingSettings(
        model_router_url="http://model-router.internal.test",
        vector_router_url="http://vector-router.internal.test",
        asset_embedding_completed_topic=COMPLETED_TOPIC,
    )


def _chunk(order_index: int, **overrides) -> dict:
    chunk = {
        "id": f"chunk-{order_index}",
        "batch_id": BATCH_ID,
        "document_id": DOCUMENT_ID,
        "text": f"body-{order_index}",
        "order_index": order_index,
        "start_char": order_index * 10,
        "end_char": order_index * 10 + 9,
        "token_count": 12,
        "page_number": order_index,
        "section_hint": "Introduction",
    }
    chunk.update(overrides)
    return chunk


def _envelope(chunk_start: int = 0, chunk_end: int = 40) -> AssetEmbeddingEnvelope:
    return AssetEmbeddingEnvelope(
        batch_id=BATCH_ID,
        document_id=DOCUMENT_ID,
        correlation_id=CORRELATION_ID,
        payload=AssetEmbeddingPayload(
            document_id=DOCUMENT_ID,
            chunk_start=chunk_start,
            chunk_end=chunk_end,
            unit_index=2,
            unit_count=5,
        ),
    )


def _default_vector_client() -> AsyncMock:
    client = AsyncMock()
    client.embed = AsyncMock(side_effect=lambda texts: [[0.1] for _ in texts])
    client.upsert = AsyncMock(return_value=0)
    return client


def _handler(chunks: list[dict], vector_client=None, publisher=None):
    chunk_repo = AsyncMock()
    chunk_repo.get_range = AsyncMock(return_value=chunks)
    if vector_client is None:
        vector_client = _default_vector_client()
    publisher = publisher or AsyncMock()
    deps = EmbedAssetChunksDeps(
        chunk_repo=chunk_repo,
        vector_client=vector_client,
        publisher=publisher,
        settings=_settings(),
    )
    return EmbedAssetChunksHandler(deps), vector_client, publisher


async def test_payload_fidelity_and_vector_id():
    handler, vector_client, _ = _handler([_chunk(0)])

    outcome = await handler.handle(_envelope())

    assert outcome == ProcessOutcome.SUCCESS
    records = vector_client.upsert.await_args.args[0]
    assert len(records) == 1
    record = records[0]
    assert record["id"] == str(uuid5(ASSET_NS, "chunk-0"))
    payload = record["payload"]
    assert payload["chunk_id"] == "chunk-0"
    assert payload["document_id"] == DOCUMENT_ID
    assert payload["batch_id"] == BATCH_ID
    assert payload["order_index"] == 0
    assert payload["start_char"] == 0
    assert payload["end_char"] == 9
    assert payload["page_number"] == 0
    assert payload["section_label"] == "Introduction"
    assert payload["token_count"] == 12
    assert payload["text_excerpt"] == "body-0"


async def test_upsert_uses_asset_target():
    handler, vector_client, _ = _handler([_chunk(0)])

    await handler.handle(_envelope())

    assert vector_client.upsert.await_args.kwargs["target"] == "asset"


async def test_page_number_null_becomes_minus_one():
    handler, vector_client, _ = _handler([_chunk(0, page_number=None)])

    await handler.handle(_envelope())

    payload = vector_client.upsert.await_args.args[0][0]["payload"]
    assert payload["page_number"] == -1


async def test_section_label_from_file_path_when_no_hint():
    chunk = _chunk(0, section_hint="", metadata={"file_path": "src/pkg/module.py"})
    handler, vector_client, _ = _handler([chunk])

    await handler.handle(_envelope())

    payload = vector_client.upsert.await_args.args[0][0]["payload"]
    assert payload["section_label"] == "module.py"


async def test_truncation_rule_only_affects_embedded_text():
    text = "x" * 1000
    chunk = _chunk(0, text=text, token_count=16000)
    captured = {}

    async def _embed(texts):
        captured["texts"] = texts
        return [[0.1] for _ in texts]

    vector_client = AsyncMock()
    vector_client.embed = AsyncMock(side_effect=_embed)
    handler, vector_client, _ = _handler([chunk], vector_client=vector_client)

    await handler.handle(_envelope())

    assert len(captured["texts"][0]) == 500
    payload = vector_client.upsert.await_args.args[0][0]["payload"]
    assert payload["text_excerpt"] == text[:512]
    assert payload["token_count"] == 16000


async def test_bounded_batching_embed_and_upsert():
    chunks = [_chunk(i) for i in range(40)]
    handler, vector_client, _ = _handler(chunks)

    await handler.handle(_envelope())

    embed_sizes = [len(call.args[0]) for call in vector_client.embed.await_args_list]
    assert embed_sizes == [16, 16, 8]
    upsert_sizes = [len(call.args[0]) for call in vector_client.upsert.await_args_list]
    assert upsert_sizes == [40]
    assert all(size <= 100 for size in upsert_sizes)


async def test_completed_event_published_with_unit_fields():
    handler, _, publisher = _handler([_chunk(0), _chunk(1)])

    await handler.handle(_envelope())

    topic = publisher.publish.await_args.args[0]
    event = publisher.publish.await_args.args[1]
    assert topic == COMPLETED_TOPIC
    assert publisher.publish.await_args.kwargs["session_id"] == BATCH_ID
    assert event["event_type"] == "asset-embedding.completed"
    assert event["payload"]["unit_index"] == 2
    assert event["payload"]["unit_count"] == 5
    assert event["payload"]["document_id"] == DOCUMENT_ID
    assert event["payload"]["chunk_count"] == 2


async def test_empty_chunk_range_publishes_zero_and_succeeds():
    handler, vector_client, publisher = _handler([])

    outcome = await handler.handle(_envelope())

    assert outcome == ProcessOutcome.SUCCESS
    vector_client.embed.assert_not_awaited()
    vector_client.upsert.assert_not_awaited()
    assert publisher.publish.await_args.args[1]["payload"]["chunk_count"] == 0


async def test_transient_vector_error_classified_transient():
    vector_client = AsyncMock()
    vector_client.embed = AsyncMock(side_effect=VectorRouterTransientError("down"))
    handler, _, _ = _handler([_chunk(0)], vector_client=vector_client)

    outcome = await handler.handle(_envelope())

    assert outcome == ProcessOutcome.TRANSIENT


async def test_permanent_vector_error_classified_permanent():
    vector_client = AsyncMock()
    vector_client.embed = AsyncMock(side_effect=VectorRouterPermanentError("bad", status_code=400))
    handler, _, _ = _handler([_chunk(0)], vector_client=vector_client)

    outcome = await handler.handle(_envelope())

    assert outcome == ProcessOutcome.PERMANENT


@pytest.mark.parametrize("chunk_count", [1, 50])
async def test_all_chunks_upserted(chunk_count):
    chunks = [_chunk(i) for i in range(chunk_count)]
    handler, vector_client, _ = _handler(chunks)

    await handler.handle(_envelope())

    total = sum(len(call.args[0]) for call in vector_client.upsert.await_args_list)
    assert total == chunk_count
