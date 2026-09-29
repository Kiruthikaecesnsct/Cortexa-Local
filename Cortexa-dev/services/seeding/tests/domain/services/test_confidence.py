from seeding.domain.enums.scoring_axis import ScoringAxis
from seeding.domain.models.axis_score import AxisScore
from seeding.domain.services.confidence import calculate_confidence_score


def test_calculate_confidence_score_both_axes_present():
    axes = {
        ScoringAxis.Novelty: AxisScore(axis=ScoringAxis.Novelty, score=80, refs=["ref1"]),
        ScoringAxis.Patentability: AxisScore(
            axis=ScoringAxis.Patentability, score=90, refs=["ref2"]
        ),
    }

    result = calculate_confidence_score(axes)

    assert result == 85.0


def test_calculate_confidence_score_fractional_average():
    axes = {
        ScoringAxis.Novelty: AxisScore(axis=ScoringAxis.Novelty, score=75, refs=["ref1"]),
        ScoringAxis.Patentability: AxisScore(
            axis=ScoringAxis.Patentability, score=80, refs=["ref2"]
        ),
    }

    result = calculate_confidence_score(axes)

    assert result == 77.5


def test_calculate_confidence_score_missing_novelty():
    axes = {
        ScoringAxis.Patentability: AxisScore(
            axis=ScoringAxis.Patentability, score=90, refs=["ref2"]
        ),
    }

    result = calculate_confidence_score(axes)

    assert result == 0.0


def test_calculate_confidence_score_missing_patentability():
    axes = {
        ScoringAxis.Novelty: AxisScore(axis=ScoringAxis.Novelty, score=80, refs=["ref1"]),
    }

    result = calculate_confidence_score(axes)

    assert result == 0.0


def test_calculate_confidence_score_empty_axes():
    axes = {}

    result = calculate_confidence_score(axes)

    assert result == 0.0


def test_calculate_confidence_score_extra_axes_ignored():
    axes = {
        ScoringAxis.Novelty: AxisScore(axis=ScoringAxis.Novelty, score=70, refs=["ref1"]),
        ScoringAxis.Patentability: AxisScore(
            axis=ScoringAxis.Patentability, score=80, refs=["ref2"]
        ),
        ScoringAxis.Feasibility: AxisScore(axis=ScoringAxis.Feasibility, score=50, refs=["ref3"]),
    }

    result = calculate_confidence_score(axes)

    assert result == 75.0
