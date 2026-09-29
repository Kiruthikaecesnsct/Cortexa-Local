from harvesting.domain.services.recommendation import (
    INVESTIGATE_THRESHOLD,
    PURSUE_THRESHOLD,
    recommendation_for,
)

SCORE_ZERO = 0.0
SCORE_BELOW_INVESTIGATE = 39.9
SCORE_INVESTIGATE_THRESHOLD = 40.0
SCORE_BETWEEN_INVESTIGATE_AND_PURSUE = 50.0
SCORE_BELOW_PURSUE = 69.9
SCORE_PURSUE_THRESHOLD = 70.0
SCORE_MAXIMUM = 100.0


def test_score_zero_returns_abandon():
    result = recommendation_for(SCORE_ZERO)

    assert result == "abandon"


def test_score_below_investigate_threshold_returns_abandon():
    result = recommendation_for(SCORE_BELOW_INVESTIGATE)

    assert result == "abandon"


def test_score_at_investigate_threshold_returns_investigate():
    result = recommendation_for(SCORE_INVESTIGATE_THRESHOLD)

    assert result == "investigate"


def test_score_between_thresholds_returns_investigate():
    result = recommendation_for(SCORE_BETWEEN_INVESTIGATE_AND_PURSUE)

    assert result == "investigate"


def test_score_below_pursue_threshold_returns_investigate():
    result = recommendation_for(SCORE_BELOW_PURSUE)

    assert result == "investigate"


def test_score_at_pursue_threshold_returns_pursue():
    result = recommendation_for(SCORE_PURSUE_THRESHOLD)

    assert result == "pursue"


def test_score_maximum_returns_pursue():
    result = recommendation_for(SCORE_MAXIMUM)

    assert result == "pursue"


def test_thresholds_have_expected_values():
    assert PURSUE_THRESHOLD == 70.0
    assert INVESTIGATE_THRESHOLD == 40.0
