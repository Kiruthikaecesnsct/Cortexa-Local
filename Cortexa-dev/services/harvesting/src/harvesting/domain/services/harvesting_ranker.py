from harvesting.domain.enums.scoring_axis import ScoringAxis
from harvesting.domain.errors.harvesting_errors import AxisMissingError
from harvesting.domain.models.axis_scores import AxisScores
from harvesting.domain.models.rank_weights import RankWeights
from harvesting.domain.models.ranked_candidate import RankedCandidate
from harvesting.domain.models.scored_candidate import ScoredCandidate

_AXIS_LABELS: dict[ScoringAxis, str] = {
    ScoringAxis.Novelty: "Novelty",
    # Inventiveness maps to Feasibility in the ranker weight schema
    ScoringAxis.Inventiveness: "Feasibility",
    ScoringAxis.Strategic: "Strategic",
    ScoringAxis.Patentability: "Patentability",
}

_RANKED_AXES = [
    ScoringAxis.Novelty,
    ScoringAxis.Inventiveness,
    ScoringAxis.Strategic,
    ScoringAxis.Patentability,
]


def _get_axis_score(candidate: ScoredCandidate, axis: ScoringAxis) -> int:
    if axis not in candidate.axes:
        raise AxisMissingError(axis.value)
    return candidate.axes[axis].score


def _rationale_segment(label: str, raw: int, weight: float) -> str:
    display = raw / 10
    contribution = raw * weight / 10
    pct = weight * 100
    return f"{label} ({display:.1f}/10, {pct:.0f}% weight): {contribution:.2f} points"


def _build_rationale(scores: AxisScores, weights: RankWeights) -> str:
    segments = [
        _rationale_segment("Novelty", scores.novelty, weights.novelty),
        _rationale_segment("Feasibility", scores.feasibility, weights.feasibility),
        _rationale_segment("Strategic", scores.strategic, weights.strategic),
        _rationale_segment("Patentability", scores.patentability, weights.patentability),
    ]
    return "; ".join(segments)


def _compute_weighted_score(scores: AxisScores, weights: RankWeights) -> float:
    return (
        scores.novelty * weights.novelty
        + scores.feasibility * weights.feasibility
        + scores.strategic * weights.strategic
        + scores.patentability * weights.patentability
    )


def _score_candidates(
    candidates: list[ScoredCandidate], weights: RankWeights
) -> list[tuple[ScoredCandidate, AxisScores, float]]:
    scored: list[tuple[ScoredCandidate, AxisScores, float]] = []
    for candidate in candidates:
        axis_scores = AxisScores(
            novelty=_get_axis_score(candidate, ScoringAxis.Novelty),
            feasibility=_get_axis_score(candidate, ScoringAxis.Inventiveness),
            strategic=_get_axis_score(candidate, ScoringAxis.Strategic),
            patentability=_get_axis_score(candidate, ScoringAxis.Patentability),
        )
        weighted = _compute_weighted_score(axis_scores, weights)
        scored.append((candidate, axis_scores, weighted))
    scored.sort(key=lambda x: x[2], reverse=True)
    return scored


def _build_ranked_list(
    scored: list[tuple[ScoredCandidate, AxisScores, float]], weights: RankWeights
) -> list[RankedCandidate]:
    result: list[RankedCandidate] = []
    for position, (candidate, axis_scores, weighted) in enumerate(scored, start=1):
        rationale = _build_rationale(axis_scores, weights)
        result.append(
            RankedCandidate(
                candidate_id=candidate.candidate_id,
                batch_id=candidate.batch_id,
                document_id=candidate.document_id,
                rank=position,
                weighted_score=weighted,
                novelty_score=axis_scores.novelty,
                feasibility_score=axis_scores.feasibility,
                strategic_score=axis_scores.strategic,
                patentability_score=axis_scores.patentability,
                rationale=rationale,
            )
        )
    return result


def rank(candidates: list[ScoredCandidate], weights: RankWeights) -> list[RankedCandidate]:
    return _build_ranked_list(_score_candidates(candidates, weights), weights)
