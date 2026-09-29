import logging
import math
from dataclasses import dataclass
from uuid import UUID, uuid5

from seeding.application.handlers.process_seeding_request_handler import ProcessOutcome
from seeding.domain.errors.seeding_errors import VectorRouterPermanentError
from seeding.domain.events.asset_embedding import (
    AssetEmbeddingEnvelope,
    make_asset_embedding_completed_event,
)
from seeding.domain.ports.chunk_read_port import ChunkReadPort
from seeding.domain.ports.event_publisher_port import EventPublisherPort
from seeding.domain.ports.vector_router_port import VectorRouterPort
from seeding.domain.services.chunk_grouping import page_number as _page_number
from seeding.domain.services.chunk_grouping import section_label as _section_label
from seeding.infrastructure.config.settings import SeedingSettings

logger = logging.getLogger(__name__)

ASSET_NS = UUID("26be4caf-9785-5651-b043-e7f150242644")
_MAX_EMBED_TOKENS = 8000
_EXCERPT_LEN = 512
_ASSET_TARGET = "asset"
_PERMANENT_ERRORS = (VectorRouterPermanentError,)


@dataclass(frozen=True)
class _ChunkItem:
    payload: dict
    embed_text: str


@dataclass
class EmbedAssetChunksDeps:
    chunk_repo: ChunkReadPort
    vector_client: VectorRouterPort
    publisher: EventPublisherPort
    settings: SeedingSettings


def _truncate_for_embed(text: str, token_count: int) -> str:
    if token_count <= _MAX_EMBED_TOKENS:
        return text
    keep = max(1, math.floor(len(text) * _MAX_EMBED_TOKENS / token_count))
    return text[:keep]


def _build_chunk_item(chunk: dict, batch_id: str) -> _ChunkItem:
    chunk_id = str(chunk["id"])
    text = str(chunk.get("text") or "")
    token_count = int(chunk.get("token_count") or 0)
    payload = {
        "chunk_id": chunk_id,
        "document_id": str(chunk.get("document_id") or ""),
        "batch_id": batch_id,
        "order_index": int(chunk.get("order_index") or 0),
        "start_char": int(chunk.get("start_char") or 0),
        "end_char": int(chunk.get("end_char") or 0),
        "page_number": _page_number(chunk),
        "section_label": _section_label(chunk),
        "token_count": token_count,
        "text_excerpt": text[:_EXCERPT_LEN],
    }
    return _ChunkItem(payload=payload, embed_text=_truncate_for_embed(text, token_count))


def _vector_id(chunk_id: str) -> str:
    return str(uuid5(ASSET_NS, chunk_id))


class EmbedAssetChunksHandler:
    def __init__(self, deps: EmbedAssetChunksDeps) -> None:
        self._deps = deps

    async def handle(self, envelope: AssetEmbeddingEnvelope) -> ProcessOutcome:
        try:
            return await self._execute(envelope)
        except Exception as exc:
            return self._classify_error(exc, envelope)

    async def _execute(self, envelope: AssetEmbeddingEnvelope) -> ProcessOutcome:
        payload = envelope.payload
        chunks = await self._deps.chunk_repo.get_range(
            envelope.batch_id, payload.document_id, payload.chunk_start, payload.chunk_end
        )
        if chunks:
            items = [_build_chunk_item(chunk, envelope.batch_id) for chunk in chunks]
            vectors = await self._embed_all([item.embed_text for item in items])
            await self._upsert_all(items, vectors)
        await self._publish_completed(envelope, len(chunks))
        return ProcessOutcome.SUCCESS

    async def _embed_all(self, texts: list[str]) -> list[list[float]]:
        batch_size = self._deps.settings.embed_batch_size
        vectors: list[list[float]] = []
        for start in range(0, len(texts), batch_size):
            vectors.extend(await self._deps.vector_client.embed(texts[start : start + batch_size]))
        return vectors

    async def _upsert_all(self, items: list[_ChunkItem], vectors: list[list[float]]) -> None:
        records = [
            {"id": _vector_id(item.payload["chunk_id"]), "vector": vector, "payload": item.payload}
            for item, vector in zip(items, vectors, strict=True)
        ]
        batch_size = self._deps.settings.upsert_batch_size
        for start in range(0, len(records), batch_size):
            await self._deps.vector_client.upsert(
                records[start : start + batch_size], target=_ASSET_TARGET
            )

    async def _publish_completed(self, envelope: AssetEmbeddingEnvelope, chunk_count: int) -> None:
        payload = envelope.payload
        event = make_asset_embedding_completed_event(
            batch_id=envelope.batch_id,
            document_id=payload.document_id,
            unit_index=payload.unit_index,
            unit_count=payload.unit_count,
            chunk_count=chunk_count,
            correlation_id=envelope.correlation_id,
        )
        await self._deps.publisher.publish(
            self._deps.settings.asset_embedding_completed_topic,
            event,
            session_id=envelope.batch_id,
            correlation_id=envelope.correlation_id,
        )
        logger.info(
            "batch_id=%s document_id=%s unit_index=%d unit_count=%d chunk_count=%d outcome=SUCCESS",
            envelope.batch_id,
            payload.document_id,
            payload.unit_index,
            payload.unit_count,
            chunk_count,
        )

    def _classify_error(self, exc: Exception, envelope: AssetEmbeddingEnvelope) -> ProcessOutcome:
        outcome = (
            ProcessOutcome.PERMANENT
            if isinstance(exc, _PERMANENT_ERRORS)
            else ProcessOutcome.TRANSIENT
        )
        status_code = getattr(exc, "status_code", None)
        logger.error(
            "batch_id=%s document_id=%s unit_index=%d outcome=%s status_code=%s error_class=%s",
            envelope.batch_id,
            envelope.payload.document_id,
            envelope.payload.unit_index,
            outcome.value,
            status_code,
            type(exc).__name__,
        )
        return outcome
