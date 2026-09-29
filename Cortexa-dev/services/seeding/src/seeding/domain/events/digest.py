from datetime import UTC, datetime
from uuid import uuid4

from pydantic import BaseModel, Field

REQUESTED_EVENT_TYPE = "digest.requested"
COMPLETED_EVENT_TYPE = "digest.completed"
_SCHEMA_VERSION = "1.0"


class DigestPayload(BaseModel):
    document_id: str
    unit_index: int = 0
    unit_count: int = 1
    ai_model: str | None = None


class DigestEnvelope(BaseModel):
    event_id: str = Field(default_factory=lambda: str(uuid4()))
    schema_version: str = _SCHEMA_VERSION
    event_type: str = REQUESTED_EVENT_TYPE
    batch_id: str
    document_id: str | None = None
    correlation_id: str | None = None
    occurred_at: datetime = Field(default_factory=lambda: datetime.now(UTC))
    payload: DigestPayload


def make_digest_completed_event(
    batch_id: str,
    document_id: str,
    empty: bool,
    section_count: int,
    brief_id: str,
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
            "empty": empty,
            "section_count": section_count,
            "brief_id": brief_id,
        },
    }
