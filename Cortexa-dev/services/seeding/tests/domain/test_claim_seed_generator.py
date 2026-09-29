import pytest

from seeding.domain.enums.scoring_axis import ScoringAxis
from seeding.domain.errors.seeding_errors import (
    InsufficientClaimSeedsError,
    UngroundedSeedingError,
)
from seeding.domain.models.axis_score import AxisScore
from seeding.domain.models.claim_seed import (
    ClaimSeedSet,
    DependentClaimSeed,
    IndependentClaimSeed,
    Limitation,
)
from seeding.domain.models.evidence_bundle import EvidenceBundle, EvidenceHit
from seeding.domain.models.idf_draft import IdfDraft
from seeding.domain.models.idf_section import IdfSection
from seeding.domain.models.scored_candidate import ScoredCandidate
from seeding.domain.services.claim_seed_generator import validate_inputs, validate_seed_counts


def _make_axes() -> dict[ScoringAxis, AxisScore]:
    return {axis: AxisScore(axis=axis, score=80, refs=["[src1:0-10]"]) for axis in ScoringAxis}


def _make_candidate(axes: dict[ScoringAxis, AxisScore] | None = None) -> ScoredCandidate:
    return ScoredCandidate(
        candidate_id="cand-1",
        batch_id="batch-1",
        job_id="job-1",
        document_id="doc-1",
        axes=axes if axes is not None else _make_axes(),
    )


def _make_bundle(hits: list[EvidenceHit] | None = None) -> EvidenceBundle:
    default_hits = [EvidenceHit(source_id="src1", text="evidence text", start=0, end=100)]
    return EvidenceBundle(
        id="bundle-1",
        batch_id="batch-1",
        job_id="job-1",
        candidate_id="cand-1",
        document_id="doc-1",
        hits=hits if hits is not None else default_hits,
        source_flags=[],
    )


def _make_section(text: str = "section text") -> IdfSection:
    return IdfSection(text=text, citations=["[src1:0-10]"])


def _make_idf_draft(
    abstract: str = "abstract text",
    summary: str = "summary text",
    core_feature: str = "core feature text",
) -> IdfDraft:
    return IdfDraft(
        candidate_id="cand-1",
        batch_id="batch-1",
        job_id="job-1",
        document_id="doc-1",
        abstract=_make_section(abstract),
        background=_make_section("background text"),
        summary=_make_section(summary),
        core_differentiating_feature=_make_section(core_feature),
    )


def _make_limitation() -> Limitation:
    return Limitation(text="a limitation", evidence_refs=["[src1:0-10]"])


def _make_independent_claim() -> IndependentClaimSeed:
    return IndependentClaimSeed(
        claim_type="method",
        preamble="A method comprising",
        recitations=["recitation one"],
        limitations=[_make_limitation()],
    )


def _make_dependent_claim(parent_index: int = 0) -> DependentClaimSeed:
    return DependentClaimSeed(parent_index=parent_index, added_limitations=[_make_limitation()])


def _make_seed_set(
    independent_claims: list[IndependentClaimSeed] | None = None,
    dependent_claims: list[DependentClaimSeed] | None = None,
) -> ClaimSeedSet:
    return ClaimSeedSet(
        candidate_id="cand-1",
        batch_id="batch-1",
        document_id="doc-1",
        independent_claims=(
            independent_claims if independent_claims is not None else [_make_independent_claim()]
        ),
        dependent_claims=(
            dependent_claims
            if dependent_claims is not None
            else [_make_dependent_claim(), _make_dependent_claim()]
        ),
    )


def test_empty_bundle_raises_ungrounded():
    with pytest.raises(UngroundedSeedingError):
        validate_inputs(_make_candidate(), _make_idf_draft(), _make_bundle(hits=[]))


def test_missing_axis_raises_ungrounded():
    axes = {ScoringAxis.Novelty: AxisScore(axis=ScoringAxis.Novelty, score=80, refs=[])}
    with pytest.raises(UngroundedSeedingError):
        validate_inputs(_make_candidate(axes=axes), _make_idf_draft(), _make_bundle())


def test_empty_abstract_raises_ungrounded():
    with pytest.raises(UngroundedSeedingError):
        validate_inputs(_make_candidate(), _make_idf_draft(abstract=""), _make_bundle())


def test_empty_summary_raises_ungrounded():
    with pytest.raises(UngroundedSeedingError):
        validate_inputs(_make_candidate(), _make_idf_draft(summary=""), _make_bundle())


def test_empty_core_differentiating_feature_raises_ungrounded():
    with pytest.raises(UngroundedSeedingError):
        validate_inputs(_make_candidate(), _make_idf_draft(core_feature=""), _make_bundle())


def test_valid_inputs_does_not_raise():
    validate_inputs(_make_candidate(), _make_idf_draft(), _make_bundle())


def test_zero_independent_claims_raises_insufficient():
    seed_set = _make_seed_set(independent_claims=[])

    with pytest.raises(InsufficientClaimSeedsError):
        validate_seed_counts(seed_set)


def test_one_dependent_claim_raises_insufficient():
    seed_set = _make_seed_set(dependent_claims=[_make_dependent_claim()])

    with pytest.raises(InsufficientClaimSeedsError):
        validate_seed_counts(seed_set)


def test_zero_dependent_claims_raises_insufficient():
    seed_set = _make_seed_set(dependent_claims=[])

    with pytest.raises(InsufficientClaimSeedsError):
        validate_seed_counts(seed_set)


def test_sufficient_seed_counts_does_not_raise():
    seed_set = _make_seed_set(
        independent_claims=[_make_independent_claim()],
        dependent_claims=[_make_dependent_claim(), _make_dependent_claim()],
    )

    validate_seed_counts(seed_set)
