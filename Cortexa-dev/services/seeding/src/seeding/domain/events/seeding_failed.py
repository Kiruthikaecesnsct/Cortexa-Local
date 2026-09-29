from datetime import UTC, datetime
from uuid import uuid4

from pydantic import BaseModel

EVENT_TYPE = "seeding.failed"


class SeedingFailedPayload(BaseModel):
    reason: str
    document_id: str | None
    job_id: str
    trigger_type: str


def make_seeding_failed_event(
    batch_id: str,
    document_id: str | None,
    reason: str,
    job_id: str,
    trigger_type: str,
    correlation_id: str | None,
) -> dict:
    payload = SeedingFailedPayload(
        reason=reason,
        document_id=document_id,
        job_id=job_id,
        trigger_type=trigger_type,
    )
    return {
        "event_id": str(uuid4()),
        "schema_version": "1.0",
        "event_type": EVENT_TYPE,
        "batch_id": batch_id,
        "document_id": document_id,
        "correlation_id": correlation_id,
        "occurred_at": datetime.now(UTC).isoformat(),
        "payload": payload.model_dump(),
    }
