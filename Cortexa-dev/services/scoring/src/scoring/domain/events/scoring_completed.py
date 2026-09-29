from scoring.domain.events.event_envelope import EventEnvelope
from scoring.domain.models.stored_verdict import StoredVerdict


def make_scoring_completed_event(
    verdict: StoredVerdict,
    correlation_id: str | None = None,
) -> EventEnvelope:
    payload = {
        "document_id": verdict.document_id,
        "candidate_id": verdict.candidate_id,
        "verdict_id": verdict.id,
        "composite_score": verdict.composite_score,
        "grounding_meets_minimum": verdict.grounding_meets_minimum,
        "grounding_source_count": verdict.grounding_source_count,
        "agreement_level": verdict.agreement_level,
        "single_reason": verdict.single_reason,
    }
    return EventEnvelope(
        event_type="scoring.completed",
        batch_id=verdict.batch_id,
        document_id=verdict.document_id,
        correlation_id=correlation_id,
        payload=payload,
    )
