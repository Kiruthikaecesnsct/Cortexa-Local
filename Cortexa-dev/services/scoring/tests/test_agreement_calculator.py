from scoring.domain.enums.agreement_level import AgreementLevel
from scoring.domain.enums.scoring_axis import ScoringAxis
from scoring.domain.models.axis_score import AxisScore
from scoring.domain.models.scoring_verdict import ScoringVerdict
from scoring.domain.services.agreement_calculator import compute_agreement

_AXES = list(ScoringAxis)

_BASE_SCORES = {
    ScoringAxis.Novelty: 80,
    ScoringAxis.Inventiveness: 70,
    ScoringAxis.Commercial: 60,
    ScoringAxis.Strategic: 50,
    ScoringAxis.Patentability: 90,
}


def _make_verdict(scores: dict[ScoringAxis, int]) -> ScoringVerdict:
    axes = {axis: AxisScore(axis=axis, score=score, refs=["E1"]) for axis, score in scores.items()}
    return ScoringVerdict(
        batch_id="batch-001",
        job_id="job-001",
        candidate_id="cand-001",
        document_id="doc-001",
        axes=axes,
    )


FULL_AGREEMENT_COUNT = 5
PARTIAL_COUNT_4 = 4
PARTIAL_COUNT_3 = 3
NO_AGREEMENT_COUNT_2 = 2
NO_AGREEMENT_COUNT_0 = 0
TOLERANCE = 10


def _primary_verdict() -> ScoringVerdict:
    return _make_verdict(_BASE_SCORES)


def test_compute_agreement_all_axes_within_tolerance_returns_full():
    primary = _primary_verdict()
    secondary = _make_verdict(_BASE_SCORES)

    level, count = compute_agreement(primary, secondary)

    assert level == AgreementLevel.Full
    assert count == FULL_AGREEMENT_COUNT


def test_compute_agreement_four_axes_within_tolerance_returns_partial():
    shifted = {**_BASE_SCORES, ScoringAxis.Novelty: _BASE_SCORES[ScoringAxis.Novelty] + 11}
    primary = _primary_verdict()
    secondary = _make_verdict(shifted)

    level, count = compute_agreement(primary, secondary)

    assert level == AgreementLevel.Partial
    assert count == PARTIAL_COUNT_4


def test_compute_agreement_three_axes_within_tolerance_returns_partial():
    shifted = {
        **_BASE_SCORES,
        ScoringAxis.Novelty: _BASE_SCORES[ScoringAxis.Novelty] + 11,
        ScoringAxis.Inventiveness: _BASE_SCORES[ScoringAxis.Inventiveness] + 11,
    }
    primary = _primary_verdict()
    secondary = _make_verdict(shifted)

    level, count = compute_agreement(primary, secondary)

    assert level == AgreementLevel.Partial
    assert count == PARTIAL_COUNT_3


def test_compute_agreement_two_axes_within_tolerance_returns_no_agreement():
    shifted = {
        **_BASE_SCORES,
        ScoringAxis.Novelty: _BASE_SCORES[ScoringAxis.Novelty] + 11,
        ScoringAxis.Inventiveness: _BASE_SCORES[ScoringAxis.Inventiveness] + 11,
        ScoringAxis.Commercial: _BASE_SCORES[ScoringAxis.Commercial] + 11,
    }
    primary = _primary_verdict()
    secondary = _make_verdict(shifted)

    level, count = compute_agreement(primary, secondary)

    assert level == AgreementLevel.NoAgreement
    assert count == NO_AGREEMENT_COUNT_2


def test_compute_agreement_zero_axes_within_tolerance_returns_no_agreement():
    shifted = {axis: score - 11 for axis, score in _BASE_SCORES.items()}
    primary = _primary_verdict()
    secondary = _make_verdict(shifted)

    level, count = compute_agreement(primary, secondary)

    assert level == AgreementLevel.NoAgreement
    assert count == NO_AGREEMENT_COUNT_0


def test_compute_agreement_boundary_difference_exactly_10_counts_as_agreeing():
    shifted = {**_BASE_SCORES, ScoringAxis.Novelty: _BASE_SCORES[ScoringAxis.Novelty] + TOLERANCE}
    primary = _primary_verdict()
    secondary = _make_verdict(shifted)

    level, count = compute_agreement(primary, secondary)

    assert count == FULL_AGREEMENT_COUNT
    assert level == AgreementLevel.Full


def test_compute_agreement_boundary_difference_11_does_not_agree():
    shifted = {
        **_BASE_SCORES,
        ScoringAxis.Novelty: _BASE_SCORES[ScoringAxis.Novelty] + (TOLERANCE + 1),
    }
    primary = _primary_verdict()
    secondary = _make_verdict(shifted)

    level, count = compute_agreement(primary, secondary)

    assert count == PARTIAL_COUNT_4
    assert level == AgreementLevel.Partial
