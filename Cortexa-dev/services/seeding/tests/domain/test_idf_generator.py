import pytest

from seeding.domain.enums.opportunity_category import OpportunityCategory
from seeding.domain.enums.scoring_axis import ScoringAxis
from seeding.domain.errors.seeding_errors import AbstractTooLongError, UngroundedSeedingError
from seeding.domain.models.axis_score import AxisScore
from seeding.domain.models.evidence_bundle import EvidenceBundle, EvidenceHit
from seeding.domain.models.opportunity import Opportunity
from seeding.domain.models.opportunity_map import OpportunityMap
from seeding.domain.models.scored_candidate import ScoredCandidate
from seeding.domain.services.idf_generator import validate_abstract_length, validate_inputs


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


def _make_opportunity_map(empty: bool = False) -> OpportunityMap:
    opportunities = {category: [] for category in OpportunityCategory}
    if not empty:
        opportunities[OpportunityCategory.Whitespace] = [
            Opportunity(
                category=OpportunityCategory.Whitespace,
                description="d",
                justification="j",
                citations=["[src1:0-10]"],
            )
        ]
    return OpportunityMap(
        candidate_id="cand-1", batch_id="batch-1", document_id="doc-1", opportunities=opportunities
    )


def test_empty_bundle_raises_ungrounded():
    with pytest.raises(UngroundedSeedingError):
        validate_inputs(_make_candidate(), _make_opportunity_map(), _make_bundle(hits=[]))


def test_missing_axis_raises_ungrounded():
    axes = {ScoringAxis.Novelty: AxisScore(axis=ScoringAxis.Novelty, score=80, refs=[])}
    with pytest.raises(UngroundedSeedingError):
        validate_inputs(_make_candidate(axes=axes), _make_opportunity_map(), _make_bundle())


def test_empty_opportunity_map_raises_ungrounded():
    with pytest.raises(UngroundedSeedingError):
        validate_inputs(_make_candidate(), _make_opportunity_map(empty=True), _make_bundle())


def test_valid_inputs_does_not_raise():
    validate_inputs(_make_candidate(), _make_opportunity_map(), _make_bundle())


def test_abstract_within_limit_does_not_raise():
    validate_abstract_length("word " * 250)


def test_abstract_over_limit_raises():
    with pytest.raises(AbstractTooLongError):
        validate_abstract_length("word " * 251)
