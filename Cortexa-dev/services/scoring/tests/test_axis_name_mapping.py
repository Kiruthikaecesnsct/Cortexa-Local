from scoring.domain.enums.scoring_axis import ScoringAxis
from scoring.domain.services.axis_reasoning import axis_name_for_frontend


def test_novelty_maps_to_novelty():
    result = axis_name_for_frontend(ScoringAxis.Novelty)
    assert result == "novelty"


def test_inventiveness_maps_to_non_obviousness():
    result = axis_name_for_frontend(ScoringAxis.Inventiveness)
    assert result == "non_obviousness"


def test_patentability_maps_to_claim_clarity():
    result = axis_name_for_frontend(ScoringAxis.Patentability)
    assert result == "claim_clarity"


def test_strategic_maps_to_enablement():
    result = axis_name_for_frontend(ScoringAxis.Strategic)
    assert result == "enablement"


def test_commercial_maps_to_utility():
    result = axis_name_for_frontend(ScoringAxis.Commercial)
    assert result == "utility"


def test_all_axes_have_unique_frontend_names():
    axes = [
        ScoringAxis.Novelty,
        ScoringAxis.Inventiveness,
        ScoringAxis.Patentability,
        ScoringAxis.Strategic,
        ScoringAxis.Commercial,
    ]
    frontend_names = [axis_name_for_frontend(axis) for axis in axes]
    assert len(frontend_names) == len(set(frontend_names))
