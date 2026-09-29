from pydantic import BaseModel

from evidence.domain.events.event_envelope import EventEnvelope


class EvidenceFailedPayload(BaseModel):
    reason: str
    document_id: str
    job_id: str
    candidate_id: str | None
    trigger_type: str


def make_evidence_failed_event(
    batch_id: str,
    document_id: str,
    reason: str,
    job_id: str,
    candidate_id: str | None,
    trigger_type: str,
    correlation_id: str | None = None,
) -> EventEnvelope:
    payload = EvidenceFailedPayload(
        reason=reason,
        document_id=document_id,
        job_id=job_id,
        candidate_id=candidate_id,
        trigger_type=trigger_type,
    )
    return EventEnvelope(
        event_type="evidence.failed",
        batch_id=batch_id,
        document_id=document_id,
        correlation_id=correlation_id,
        payload=payload.model_dump(),
    )
