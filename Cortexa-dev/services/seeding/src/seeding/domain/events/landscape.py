from datetime import UTC, datetime
from uuid import uuid4

from pydantic import BaseModel, Field

REQUESTED_EVENT_TYPE = "landscape.requested"
COMPLETED_EVENT_TYPE = "landscape.completed"
_SCHEMA_VERSION = "1.0"


class LandscapePayload(BaseModel):
    document_id: str
    ai_model: str | None = None


class LandscapeEnvelope(BaseModel):
    event_id: str = Field(default_factory=lambda: str(uuid4()))
    schema_version: str = _SCHEMA_VERSION
    event_type: str = REQUESTED_EVENT_TYPE
    batch_id: str
    document_id: str | None = None
    correlation_id: str | None = None
    occurred_at: datetime = Field(default_factory=lambda: datetime.now(UTC))
    payload: LandscapePayload


def make_landscape_completed_event(
    batch_id: str,
    document_id: str,
    landscape_id: str,
    concept_count: int,
    whitespace_count: int,
    corpus_only: bool,
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
            "landscape_id": landscape_id,
            "concept_count": concept_count,
            "whitespace_count": whitespace_count,
            "corpus_only": corpus_only,
        },
    }
