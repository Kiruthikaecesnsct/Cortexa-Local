import pytest

from seeding.domain.enums.scoring_axis import ScoringAxis
from seeding.domain.errors.seeding_errors import UngroundedSeedingError
from seeding.domain.models.axis_score import AxisScore
from seeding.domain.models.evidence_bundle import EvidenceBundle, EvidenceHit
from seeding.domain.models.scored_candidate import ScoredCandidate
from seeding.domain.services.opportunity_map_generator import validate_inputs


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


def test_empty_bundle_raises_ungrounded():
    candidate = _make_candidate()
    bundle = _make_bundle(hits=[])

    with pytest.raises(UngroundedSeedingError):
        validate_inputs(candidate, bundle)


def test_missing_axis_raises_ungrounded():
    axes = {ScoringAxis.Novelty: AxisScore(axis=ScoringAxis.Novelty, score=80, refs=[])}
    candidate = _make_candidate(axes=axes)
    bundle = _make_bundle()

    with pytest.raises(UngroundedSeedingError):
        validate_inputs(candidate, bundle)


def test_valid_inputs_does_not_raise():
    candidate = _make_candidate()
    bundle = _make_bundle()

    validate_inputs(candidate, bundle)
