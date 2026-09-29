from datetime import UTC, datetime
from uuid import uuid4

from pydantic import BaseModel, Field

REQUESTED_EVENT_TYPE = "asset-embedding.requested"
COMPLETED_EVENT_TYPE = "asset-embedding.completed"
_SCHEMA_VERSION = "1.0"


class AssetEmbeddingPayload(BaseModel):
    document_id: str
    chunk_start: int
    chunk_end: int
    unit_index: int
    unit_count: int


class AssetEmbeddingEnvelope(BaseModel):
    event_id: str = Field(default_factory=lambda: str(uuid4()))
    schema_version: str = _SCHEMA_VERSION
    event_type: str = REQUESTED_EVENT_TYPE
    batch_id: str
    document_id: str | None = None
    correlation_id: str | None = None
    occurred_at: datetime = Field(default_factory=lambda: datetime.now(UTC))
    payload: AssetEmbeddingPayload


def make_asset_embedding_completed_event(
    batch_id: str,
    document_id: str,
    unit_index: int,
    unit_count: int,
    chunk_count: int,
    correlation_id: str | None,
) -> dict:
    return {
        "event_id": str(uuid4()),
        "schema_version": _SCHEMA_VERSION,
        "event_type": COMPLETED_EVENT_TYPE,
        "batch_id": batch_id,
        "document_id": document_id,
        "correlation_id": correlation_id,
        "occurred_at": datetime.now(UTC).isoformat(),
        "payload": {
            "document_id": document_id,
            "unit_index": unit_index,
            "unit_count": unit_count,
            "chunk_count": chunk_count,
        },
    }
