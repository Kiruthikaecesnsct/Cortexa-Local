from harvesting.domain.enums.maturity import Maturity
from harvesting.domain.enums.scoring_axis import ScoringAxis
from harvesting.domain.errors.harvesting_errors import AxisMissingError
from harvesting.domain.models.maturity_result import MaturityBuildParams, MaturityResult
from harvesting.domain.models.scored_candidate import ScoredCandidate


def _get_score(candidate: ScoredCandidate, axis: ScoringAxis) -> int:
    entry = candidate.axes.get(axis)
    if entry is None:
        raise AxisMissingError(axis.value)
    return entry.score


def classify(
    candidate: ScoredCandidate,
    novelty_threshold: int,
    feasibility_threshold: int,
) -> MaturityResult:
    novelty = _get_score(candidate, ScoringAxis.Novelty)
    feasibility = _get_score(candidate, ScoringAxis.Patentability)
    is_mature = novelty >= novelty_threshold and feasibility >= feasibility_threshold
    maturity = Maturity.Mature if is_mature else Maturity.Emerging
    return MaturityResult.build(
        MaturityBuildParams(
            candidate_id=candidate.candidate_id,
            batch_id=candidate.batch_id,
            document_id=candidate.document_id,
            maturity=maturity,
            novelty_score=novelty,
            feasibility_score=feasibility,
            novelty_threshold=novelty_threshold,
            feasibility_threshold=feasibility_threshold,
        )
    )
