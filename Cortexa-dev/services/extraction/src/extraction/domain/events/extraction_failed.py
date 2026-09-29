from pydantic import BaseModel

from extraction.domain.events.event_envelope import EventEnvelope


class ExtractionFailedPayload(BaseModel):
    reason: str
    document_id: str | None
    job_id: str
    trigger_type: str


def make_extraction_failed_event(
    batch_id: str,
    document_id: str | None,
    reason: str,
    job_id: str,
    trigger_type: str,
    correlation_id: str,
) -> EventEnvelope:
    payload = ExtractionFailedPayload(
        reason=reason,
        document_id=document_id,
        job_id=job_id,
        trigger_type=trigger_type,
    )
    return EventEnvelope(
        event_type="extraction.failed",
        batch_id=batch_id,
        document_id=document_id,
        correlation_id=correlation_id,
        payload=payload.model_dump(),
    )
