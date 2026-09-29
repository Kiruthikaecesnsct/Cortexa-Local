from evidence.domain.enums.evidence_source import EvidenceSource
from evidence.domain.events.event_envelope import EventEnvelope
from evidence.domain.models.evidence_bundle import EvidenceBundle


def make_evidence_completed_event(
    bundle: EvidenceBundle,
    correlation_id: str | None = None,
) -> EventEnvelope:
    source_coverage = {
        "has_patent_api": bundle.source_flags.get(EvidenceSource.PatentApi, False),
        "has_corpus": bundle.source_flags.get(EvidenceSource.SeedCorpus, False),
        "has_llm": bundle.source_flags.get(EvidenceSource.LlmResearch, False),
    }
    evidence_sources = sum(1 for v in source_coverage.values() if v)
    source_status = {source.value: status.value for source, status in bundle.source_status.items()}
    payload = {
        "job_id": bundle.job_id,
        "document_id": bundle.document_id,
        "evidence_bundle_id": bundle.id,
        "candidate_id": bundle.candidate_id,
        "confidence_band": bundle.confidence_band,
        "source_coverage": source_coverage,
        "source_status": source_status,
        "sources_used": [s.value for s in bundle.sources_used],
        "evidence_sources": evidence_sources,
        "grounding": {
            "active_source_count": bundle.active_source_count,
            "minimum_required": bundle.minimum_active_sources,
            "meets_minimum": bundle.meets_minimum_sources,
        },
        "patent_coverage": {
            "degraded": bundle.degraded,
            "degraded_sources": [s.value for s in bundle.degraded_sources],
        },
        "has_llm_reasoning": bundle.llm_research is not None
        and len(bundle.llm_research.findings) > 0,
    }
    return EventEnvelope(
        event_type="evidence.completed",
        batch_id=bundle.batch_id,
        document_id=bundle.document_id,
        correlation_id=correlation_id,
        payload=payload,
    )
