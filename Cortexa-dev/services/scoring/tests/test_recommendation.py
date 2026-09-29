from scoring.domain.services.recommendation import recommendation_for


def test_score_zero_returns_abandon():
    result = recommendation_for(0.0)
    assert result == "abandon"


def test_score_just_below_investigate_threshold_returns_abandon():
    result = recommendation_for(39.9)
    assert result == "abandon"


def test_score_at_investigate_threshold_returns_investigate():
    result = recommendation_for(40.0)
    assert result == "investigate"


def test_score_just_below_pursue_threshold_returns_investigate():
    result = recommendation_for(69.9)
    assert result == "investigate"


def test_score_at_pursue_threshold_returns_pursue():
    result = recommendation_for(70.0)
    assert result == "pursue"


def test_score_max_returns_pursue():
    result = recommendation_for(100.0)
    assert result == "pursue"
