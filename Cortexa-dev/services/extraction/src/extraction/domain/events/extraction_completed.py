from pydantic import BaseModel

from extraction.domain.events.event_envelope import EventEnvelope


class ExtractionCompletedPayload(BaseModel):
    document_id: str
    candidate_ids: list[str]
    candidate_count: int
    no_candidates: bool
    job_id: str
    trigger_type: str
    unit_index: int = 0
    unit_count: int = 1


def make_extraction_completed_event(
    batch_id: str,
    document_id: str,
    candidate_ids: list[str],
    job_id: str,
    trigger_type: str,
    correlation_id: str,
    unit_index: int = 0,
    unit_count: int = 1,
) -> EventEnvelope:
    payload = ExtractionCompletedPayload(
        document_id=document_id,
        candidate_ids=candidate_ids,
        candidate_count=len(candidate_ids),
        no_candidates=len(candidate_ids) == 0,
        job_id=job_id,
        trigger_type=trigger_type,
        unit_index=unit_index,
        unit_count=unit_count,
    )
    return EventEnvelope(
        event_type="extraction.completed",
        batch_id=batch_id,
        document_id=document_id,
        correlation_id=correlation_id,
        payload=payload.model_dump(),
    )
