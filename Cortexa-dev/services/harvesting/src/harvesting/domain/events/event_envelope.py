from datetime import UTC, datetime
from uuid import uuid4

from pydantic import BaseModel, Field


class EventEnvelope(BaseModel):
    event_id: str = Field(default_factory=lambda: str(uuid4()))
    schema_version: str = "1.0"
    event_type: str | None = None
    batch_id: str
    document_id: str | None = None
    correlation_id: str | None = None
    occurred_at: datetime = Field(default_factory=lambda: datetime.now(UTC))
    payload: dict
