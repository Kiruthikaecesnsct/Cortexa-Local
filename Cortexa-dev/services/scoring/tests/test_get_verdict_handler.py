from unittest.mock import AsyncMock

import pytest

from scoring.application.handlers.get_verdict_handler import (
    GetVerdictHandler,
    GetVerdictHandlerDeps,
)
from scoring.domain.enums.agreement_level import AgreementLevel
from scoring.domain.enums.scoring_axis import ScoringAxis
from scoring.domain.errors.storage_errors import VerdictNotFoundError
from scoring.domain.models.axis_score import AxisScore
from scoring.domain.models.stored_verdict import StoredVerdict
from scoring.infrastructure.cosmos.candidate_repository import CandidateRecord
from scoring.infrastructure.cosmos.evidence_bundle_repository import EvidenceBundleRecord
from scoring.infrastructure.cosmos.seeding_repository import ClaimSeedSetRecord


def _make_stored_verdict(
    candidate_id: str = "cand-1",
    axes: dict[ScoringAxis, AxisScore] | None = None,
    composite_score: float = 65.0,
) -> StoredVerdict:
    if axes is None:
        axes = {
            ScoringAxis.Novelty: AxisScore(axis=ScoringAxis.Novelty, score=80, refs=["ref1"]),
            ScoringAxis.Inventiveness: AxisScore(
                axis=ScoringAxis.Inventiveness, score=60, refs=["ref2"]
            ),
            ScoringAxis.Patentability: AxisScore(
                axis=ScoringAxis.Patentability, score=55, refs=["ref3"]
            ),
        }
    return StoredVerdict(
        id=f"batch-1:{candidate_id}",
        batch_id="batch-1",
        job_id="job-1",
        candidate_id=candidate_id,
        document_id="doc-1",
        axes=axes,
        composite_score=composite_score,
        agreement_level=AgreementLevel.FallbackSingle,
        agreeing_axis_count=0,
    )


def _make_candidate_record(
    candidate_id: str = "cand-1",
    tech_field: str = "AI",
    problem: str = "Existing problem statement",
    mechanism: str = "A novel mechanism for adaptive processing",
) -> CandidateRecord:
    return CandidateRecord(
        id=candidate_id,
        batch_id="batch-1",
        document_id="doc-1",
        claim_text="A method for processing data using machine learning.",
        problem=problem,
        mechanism=mechanism,
        tech_field=tech_field,
        source_span={"locator": "page 5, lines 10-15", "source_kind": "pdf"},
    )


def _make_evidence_bundle(hits: list[dict] | None = None) -> EvidenceBundleRecord:
    if hits is None:
        hits = [
            {
                "sources": ["PatentApi"],
                "similarity": 0.85,
                "citation": "US123456",
            },
            {
                "sources": ["SeedCorpus"],
                "similarity": 0.75,
                "citation": "US789012",
            },
        ]
    return EvidenceBundleRecord(
        id="evidence-1",
        batch_id="batch-1",
        candidate_id="cand-1",
        hits=hits,
        sources_used=["PatentApi", "SeedCorpus"],
        source_flags={"PatentApi": True, "SeedCorpus": True, "LlmResearch": False},
    )


def _make_claim_seed_set() -> ClaimSeedSetRecord:
    return ClaimSeedSetRecord(
        candidate_id="cand-1",
        batch_id="batch-1",
        independent_claims=[{"text": "claim 1"}, {"text": "claim 2"}],
        dependent_claims=[{"text": "claim 3"}],
    )


def _make_deps(
    verdict: StoredVerdict,
    candidate: CandidateRecord,
    evidence_bundle: EvidenceBundleRecord | None = None,
    claim_seed_set: ClaimSeedSetRecord | None = None,
) -> GetVerdictHandlerDeps:
    verdict_repo = AsyncMock()
    verdict_repo.get_by_candidate.return_value = verdict

    candidate_repo = AsyncMock()
    candidate_repo.get_by_candidate_id.return_value = candidate

    evidence_repo = AsyncMock()
    evidence_repo.get_by_candidate_id.return_value = evidence_bundle

    seeding_repo = AsyncMock()
    seeding_repo.get_by_candidate_id.return_value = claim_seed_set

    return GetVerdictHandlerDeps(
        verdict_repo=verdict_repo,
        candidate_repo=candidate_repo,
        evidence_repo=evidence_repo,
        seeding_repo=seeding_repo,
    )


@pytest.mark.asyncio
async def test_commercial_potential_emitted_when_commercial_axis_present_and_tech_field_non_empty():
    axes = {
        ScoringAxis.Novelty: AxisScore(axis=ScoringAxis.Novelty, score=80, refs=[]),
        ScoringAxis.Commercial: AxisScore(axis=ScoringAxis.Commercial, score=75, refs=[]),
    }
    verdict = _make_stored_verdict(axes=axes, composite_score=77.5)
    candidate = _make_candidate_record(tech_field="AI")

    handler = GetVerdictHandler(_make_deps(verdict, candidate))
    result = await handler.handle("cand-1")

    assert result.commercial_potential is not None
    assert result.commercial_potential.level == "high"
    assert result.commercial_potential.industry == "AI"


@pytest.mark.asyncio
async def test_commercial_potential_not_emitted_when_tech_field_empty():
    axes = {
        ScoringAxis.Novelty: AxisScore(axis=ScoringAxis.Novelty, score=80, refs=[]),
        ScoringAxis.Commercial: AxisScore(axis=ScoringAxis.Commercial, score=75, refs=[]),
    }
    verdict = _make_stored_verdict(axes=axes, composite_score=77.5)
    candidate = _make_candidate_record(tech_field="")

    handler = GetVerdictHandler(_make_deps(verdict, candidate))
    result = await handler.handle("cand-1")

    assert result.commercial_potential is None


@pytest.mark.asyncio
async def test_commercial_potential_not_emitted_when_commercial_axis_absent():
    axes = {
        ScoringAxis.Novelty: AxisScore(axis=ScoringAxis.Novelty, score=80, refs=[]),
        ScoringAxis.Inventiveness: AxisScore(axis=ScoringAxis.Inventiveness, score=60, refs=[]),
    }
    verdict = _make_stored_verdict(axes=axes, composite_score=70.0)
    candidate = _make_candidate_record(tech_field="AI")

    handler = GetVerdictHandler(_make_deps(verdict, candidate))
    result = await handler.handle("cand-1")

    assert result.commercial_potential is None


@pytest.mark.asyncio
async def test_commercial_potential_level_high_when_score_at_or_above_seventy():
    axes = {
        ScoringAxis.Commercial: AxisScore(axis=ScoringAxis.Commercial, score=70, refs=[]),
    }
    verdict = _make_stored_verdict(axes=axes, composite_score=70.0)
    candidate = _make_candidate_record(tech_field="AI")

    handler = GetVerdictHandler(_make_deps(verdict, candidate))
    result = await handler.handle("cand-1")

    assert result.commercial_potential is not None
    assert result.commercial_potential.level == "high"


@pytest.mark.asyncio
async def test_commercial_potential_level_medium_when_score_at_or_above_forty():
    axes = {
        ScoringAxis.Commercial: AxisScore(axis=ScoringAxis.Commercial, score=40, refs=[]),
    }
    verdict = _make_stored_verdict(axes=axes, composite_score=40.0)
    candidate = _make_candidate_record(tech_field="Biotech")

    handler = GetVerdictHandler(_make_deps(verdict, candidate))
    result = await handler.handle("cand-1")

    assert result.commercial_potential is not None
    assert result.commercial_potential.level == "medium"


@pytest.mark.asyncio
async def test_commercial_potential_level_low_when_score_below_forty():
    axes = {
        ScoringAxis.Commercial: AxisScore(axis=ScoringAxis.Commercial, score=39, refs=[]),
    }
    verdict = _make_stored_verdict(axes=axes, composite_score=39.0)
    candidate = _make_candidate_record(tech_field="Energy")

    handler = GetVerdictHandler(_make_deps(verdict, candidate))
    result = await handler.handle("cand-1")

    assert result.commercial_potential is not None
    assert result.commercial_potential.level == "low"


@pytest.mark.asyncio
async def test_verdict_only_assembly_evidence_sources_empty_when_no_evidence_bundle():
    verdict = _make_stored_verdict()
    candidate = _make_candidate_record()

    handler = GetVerdictHandler(
        _make_deps(verdict, candidate, evidence_bundle=None, claim_seed_set=None)
    )
    result = await handler.handle("cand-1")

    assert result.evidence_sources == []
    assert result.similar_patents is None
    assert result.claim_seeds is None


@pytest.mark.asyncio
async def test_provenance_maps_real_source_span_fields():
    verdict = _make_stored_verdict()
    candidate = CandidateRecord(
        id="cand-1",
        batch_id="batch-1",
        document_id="doc-1",
        claim_text="A method for processing data.",
        problem="Problem statement",
        mechanism="Novel processing mechanism",
        tech_field="AI",
        source_span={
            "source_kind": "pdf",
            "locator": "chars:1024-2048",
            "section_hint": "Background",
            "span_start": 1024,
            "span_end": 2048,
            "page_number": 5,
            "excerpt": "This is the actual text excerpt from the chunk.",
        },
    )

    handler = GetVerdictHandler(_make_deps(verdict, candidate))
    result = await handler.handle("cand-1")

    assert result.provenance.source_document_id == "doc-1"
    assert result.provenance.page_number == 5
    assert result.provenance.span_start == 1024
    assert result.provenance.span_end == 2048
    assert result.provenance.excerpt_text == "This is the actual text excerpt from the chunk."
    assert result.provenance.source_filename is None


@pytest.mark.asyncio
async def test_provenance_handles_missing_source_span_fields():
    verdict = _make_stored_verdict()
    candidate = CandidateRecord(
        id="cand-1",
        batch_id="batch-1",
        document_id="doc-1",
        claim_text="A method for processing data.",
        problem="Problem statement",
        mechanism="Novel processing mechanism",
        tech_field="AI",
        source_span={
            "source_kind": "pdf",
            "locator": "chars:1024-2048",
        },
    )

    handler = GetVerdictHandler(_make_deps(verdict, candidate))
    result = await handler.handle("cand-1")

    assert result.provenance.source_document_id == "doc-1"
    assert result.provenance.page_number is None
    assert result.provenance.span_start is None
    assert result.provenance.span_end is None
    assert result.provenance.excerpt_text == ""
    assert result.provenance.source_filename is None


@pytest.mark.asyncio
async def test_similar_patents_populated_when_evidence_bundle_present():
    verdict = _make_stored_verdict()
    candidate = _make_candidate_record()
    evidence_bundle = _make_evidence_bundle()

    handler = GetVerdictHandler(_make_deps(verdict, candidate, evidence_bundle=evidence_bundle))
    result = await handler.handle("cand-1")

    assert result.similar_patents is not None
    assert result.similar_patents.count == 2
    assert result.similar_patents.source_count == 2


@pytest.mark.asyncio
async def test_claim_seeds_populated_when_claim_seed_set_present():
    verdict = _make_stored_verdict()
    candidate = _make_candidate_record()
    claim_seed_set = _make_claim_seed_set()

    handler = GetVerdictHandler(_make_deps(verdict, candidate, claim_seed_set=claim_seed_set))
    result = await handler.handle("cand-1")

    assert result.claim_seeds is not None
    assert result.claim_seeds.count == 2
    assert result.claim_seeds.total == 3


@pytest.mark.asyncio
async def test_not_found_error_raised_when_verdict_not_found():
    verdict_repo = AsyncMock()
    verdict_repo.get_by_candidate.side_effect = VerdictNotFoundError("cand-999")

    deps = GetVerdictHandlerDeps(
        verdict_repo=verdict_repo,
        candidate_repo=AsyncMock(),
        evidence_repo=AsyncMock(),
        seeding_repo=AsyncMock(),
    )

    handler = GetVerdictHandler(deps)

    with pytest.raises(VerdictNotFoundError):
        await handler.handle("cand-999")


@pytest.mark.asyncio
async def test_all_axis_names_mapped_correctly():
    axes = {
        ScoringAxis.Novelty: AxisScore(axis=ScoringAxis.Novelty, score=80, refs=["r1"]),
        ScoringAxis.Inventiveness: AxisScore(axis=ScoringAxis.Inventiveness, score=70, refs=["r2"]),
        ScoringAxis.Patentability: AxisScore(axis=ScoringAxis.Patentability, score=60, refs=["r3"]),
        ScoringAxis.Strategic: AxisScore(axis=ScoringAxis.Strategic, score=50, refs=["r4"]),
        ScoringAxis.Commercial: AxisScore(axis=ScoringAxis.Commercial, score=40, refs=["r5"]),
    }
    verdict = _make_stored_verdict(axes=axes, composite_score=60.0)
    candidate = _make_candidate_record(tech_field="AI")

    handler = GetVerdictHandler(_make_deps(verdict, candidate))
    result = await handler.handle("cand-1")

    expected_axis_names = {"novelty", "non_obviousness", "claim_clarity", "enablement", "utility"}
    actual_axis_names = {score.axis for score in result.axis_scores}

    assert actual_axis_names == expected_axis_names


@pytest.mark.asyncio
async def test_recommendation_based_on_composite_score():
    verdict_abandon = _make_stored_verdict(composite_score=30.0)
    verdict_investigate = _make_stored_verdict(composite_score=55.0)
    verdict_pursue = _make_stored_verdict(composite_score=80.0)
    candidate = _make_candidate_record()

    handler_abandon = GetVerdictHandler(_make_deps(verdict_abandon, candidate))
    result_abandon = await handler_abandon.handle("cand-1")
    assert result_abandon.recommendation == "abandon"

    handler_investigate = GetVerdictHandler(_make_deps(verdict_investigate, candidate))
    result_investigate = await handler_investigate.handle("cand-1")
    assert result_investigate.recommendation == "investigate"

    handler_pursue = GetVerdictHandler(_make_deps(verdict_pursue, candidate))
    result_pursue = await handler_pursue.handle("cand-1")
    assert result_pursue.recommendation == "pursue"
