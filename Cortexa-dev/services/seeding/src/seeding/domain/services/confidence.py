from statistics import mean

from seeding.domain.enums.scoring_axis import ScoringAxis
from seeding.domain.models.axis_score import AxisScore


def calculate_confidence_score(axes: dict[ScoringAxis, AxisScore]) -> float:
    novelty = axes.get(ScoringAxis.Novelty)
    patentability = axes.get(ScoringAxis.Patentability)

    if novelty is None or patentability is None:
        return 0.0

    return round(mean([novelty.score, patentability.score]), 1)
