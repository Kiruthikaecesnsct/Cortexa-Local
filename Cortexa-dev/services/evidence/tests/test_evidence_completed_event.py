from datetime import UTC, datetime

from evidence.domain.enums.confidence_band import ConfidenceBand
from evidence.domain.enums.evidence_source import EvidenceSource
from evidence.domain.enums.patent_source_name import PatentSourceName
from evidence.domain.enums.source_status import SourceStatus
from evidence.domain.events.evidence_completed import make_evidence_completed_event
from evidence.domain.models.evidence_bundle import EvidenceBundle, LlmResearchSummary

BUNDLE_ID = "bundle-evt-001"
CANDIDATE_ID = "cand-evt-001"
DOCUMENT_ID = "doc-evt-001"
BATCH_ID = "batch-evt-001"
JOB_ID = "job-evt-001"


def _make_bundle(
    source_flags: dict[EvidenceSource, bool] | None = None,
    confidence_band: ConfidenceBand = ConfidenceBand.High,
    active_source_count: int = 3,
    meets_minimum_sources: bool = True,
    minimum_active_sources: int = 2,
    source_status: dict[EvidenceSource, SourceStatus] | None = None,
    degraded: bool = False,
    degraded_sources: list[PatentSourceName] | None = None,
    llm_research: LlmResearchSummary | None = None,
) -> EvidenceBundle:
    flags = source_flags or {
        EvidenceSource.PatentApi: True,
        EvidenceSource.SeedCorpus: True,
        EvidenceSource.LlmResearch: True,
    }
    statuses = source_status or {
        source: (SourceStatus.active if active else SourceStatus.error)
        for source, active in flags.items()
    }
    sources_used = [s for s, active in flags.items() if active]
    return EvidenceBundle(
        id=BUNDLE_ID,
        batch_id=BATCH_ID,
        job_id=JOB_ID,
        candidate_id=CANDIDATE_ID,
        document_id=DOCUMENT_ID,
        hits=[],
        confidence_band=confidence_band,
        sources_used=sources_used,
        source_flags=flags,
        source_status=statuses,
        merged_at=datetime.now(UTC),
        active_source_count=active_source_count,
        meets_minimum_sources=meets_minimum_sources,
        minimum_active_sources=minimum_active_sources,
        degraded=degraded,
        degraded_sources=degraded_sources or [],
        llm_research=llm_research,
    )


def test_payload_fields():
    bundle = _make_bundle()

    event = make_evidence_completed_event(bundle)

    payload = event.payload
    assert payload["job_id"] == JOB_ID
    assert payload["document_id"] == DOCUMENT_ID
    assert payload["evidence_bundle_id"] == BUNDLE_ID
    assert payload["candidate_id"] == CANDIDATE_ID
    assert payload["confidence_band"] == ConfidenceBand.High
    assert "source_coverage" in payload
    assert "sources_used" in payload


def test_source_coverage_mapping():
    flags = {
        EvidenceSource.PatentApi: True,
        EvidenceSource.SeedCorpus: False,
        EvidenceSource.LlmResearch: True,
    }
    bundle = _make_bundle(source_flags=flags)

    event = make_evidence_completed_event(bundle)

    coverage = event.payload["source_coverage"]
    assert coverage["has_patent_api"] is True
    assert coverage["has_corpus"] is False
    assert coverage["has_llm"] is True


def test_partial_source_flags():
    flags = {
        EvidenceSource.PatentApi: False,
        EvidenceSource.SeedCorpus: False,
        EvidenceSource.LlmResearch: True,
    }
    bundle = _make_bundle(source_flags=flags, confidence_band=ConfidenceBand.Low)

    event = make_evidence_completed_event(bundle)

    coverage = event.payload["source_coverage"]
    assert coverage["has_patent_api"] is False
    assert coverage["has_corpus"] is False
    assert coverage["has_llm"] is True
    assert event.payload["confidence_band"] == ConfidenceBand.Low


def test_envelope_metadata():
    bundle = _make_bundle()

    event = make_evidence_completed_event(bundle, correlation_id="corr-123")

    assert event.batch_id == BATCH_ID
    assert event.document_id == DOCUMENT_ID
    assert event.correlation_id == "corr-123"


def test_grounding_payload_three_sources_meets_minimum():
    bundle = _make_bundle(
        active_source_count=3,
        meets_minimum_sources=True,
        minimum_active_sources=2,
    )

    event = make_evidence_completed_event(bundle)

    grounding = event.payload["grounding"]
    assert grounding["active_source_count"] == 3
    assert grounding["minimum_required"] == 2
    assert grounding["meets_minimum"] is True


def test_grounding_payload_one_source_below_minimum():
    flags = {
        EvidenceSource.PatentApi: True,
        EvidenceSource.SeedCorpus: False,
        EvidenceSource.LlmResearch: False,
    }
    bundle = _make_bundle(
        source_flags=flags,
        confidence_band=ConfidenceBand.Low,
        active_source_count=1,
        meets_minimum_sources=False,
        minimum_active_sources=2,
    )

    event = make_evidence_completed_event(bundle)

    grounding = event.payload["grounding"]
    assert grounding["active_source_count"] == 1
    assert grounding["minimum_required"] == 2
    assert grounding["meets_minimum"] is False


def test_source_status_mapping_included_in_payload():
    flags = {
        EvidenceSource.PatentApi: True,
        EvidenceSource.SeedCorpus: False,
        EvidenceSource.LlmResearch: False,
    }
    statuses = {
        EvidenceSource.PatentApi: SourceStatus.active,
        EvidenceSource.SeedCorpus: SourceStatus.empty,
        EvidenceSource.LlmResearch: SourceStatus.filtered,
    }
    bundle = _make_bundle(source_flags=flags, source_status=statuses)

    event = make_evidence_completed_event(bundle)

    source_status = event.payload["source_status"]
    assert source_status[EvidenceSource.PatentApi.value] == SourceStatus.active.value
    assert source_status[EvidenceSource.SeedCorpus.value] == SourceStatus.empty.value
    assert source_status[EvidenceSource.LlmResearch.value] == SourceStatus.filtered.value


def test_grounding_payload_two_sources_meets_minimum():
    flags = {
        EvidenceSource.PatentApi: True,
        EvidenceSource.SeedCorpus: True,
        EvidenceSource.LlmResearch: False,
    }
    bundle = _make_bundle(
        source_flags=flags,
        confidence_band=ConfidenceBand.Medium,
        active_source_count=2,
        meets_minimum_sources=True,
        minimum_active_sources=2,
    )

    event = make_evidence_completed_event(bundle)

    grounding = event.payload["grounding"]
    assert grounding["active_source_count"] == 2
    assert grounding["minimum_required"] == 2
    assert grounding["meets_minimum"] is True


def test_patent_coverage_not_degraded_by_default():
    bundle = _make_bundle()

    event = make_evidence_completed_event(bundle)

    coverage = event.payload["patent_coverage"]
    assert coverage["degraded"] is False
    assert coverage["degraded_sources"] == []


def test_patent_coverage_degraded_carries_failed_sources():
    bundle = _make_bundle(
        degraded=True,
        degraded_sources=[PatentSourceName.Lens],
    )

    event = make_evidence_completed_event(bundle)

    coverage = event.payload["patent_coverage"]
    assert coverage["degraded"] is True
    assert coverage["degraded_sources"] == [PatentSourceName.Lens.value]


def test_has_llm_reasoning_false_when_no_llm_research():
    bundle = _make_bundle(llm_research=None)

    event = make_evidence_completed_event(bundle)

    assert event.payload["has_llm_reasoning"] is False


def test_has_llm_reasoning_false_when_findings_empty():
    bundle = _make_bundle(llm_research=LlmResearchSummary(findings=[], confidence=0.5))

    event = make_evidence_completed_event(bundle)

    assert event.payload["has_llm_reasoning"] is False


def test_has_llm_reasoning_true_when_findings_present():
    bundle = _make_bundle(
        llm_research=LlmResearchSummary(findings=["why patentable"], confidence=0.82)
    )

    event = make_evidence_completed_event(bundle)

    assert event.payload["has_llm_reasoning"] is True


def test_existing_payload_keys_unchanged_when_new_fields_present():
    bundle = _make_bundle(
        degraded=True,
        degraded_sources=[PatentSourceName.USPTO],
        llm_research=LlmResearchSummary(findings=["reasoning"], confidence=0.6),
    )

    event = make_evidence_completed_event(bundle)

    payload = event.payload
    assert "source_coverage" in payload
    assert "source_status" in payload
    assert "grounding" in payload
    assert "sources_used" in payload
