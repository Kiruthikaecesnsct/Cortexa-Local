from datetime import UTC, datetime
from uuid import uuid4

from pydantic import BaseModel

EVENT_TYPE = "ideation.completed"


class IdeationCompletedPayload(BaseModel):
    document_id: str
    candidate_ids: list[str]
    candidate_count: int
    job_id: str


def make_ideation_completed_event(
    batch_id: str,
    document_id: str,
    candidate_ids: list[str],
    correlation_id: str | None,
) -> dict:
    payload = IdeationCompletedPayload(
        document_id=document_id,
        candidate_ids=candidate_ids,
        candidate_count=len(candidate_ids),
        job_id=batch_id,
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
