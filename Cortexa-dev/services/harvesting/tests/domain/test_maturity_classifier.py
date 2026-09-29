import pytest

from harvesting.domain.enums.maturity import Maturity
from harvesting.domain.enums.scoring_axis import ScoringAxis
from harvesting.domain.errors.harvesting_errors import AxisMissingError
from harvesting.domain.models.axis_score import AxisScore
from harvesting.domain.models.scored_candidate import ScoredCandidate
from harvesting.domain.services import maturity_classifier

NOVELTY_THRESHOLD = 7
FEASIBILITY_THRESHOLD = 6

CANDIDATE_ID = "cand-001"
BATCH_ID = "batch-abc"
JOB_ID = "job-xyz"
DOCUMENT_ID = "doc-001"


def _make_candidate(
    novelty_score: int,
    feasibility_score: int,
    **kwargs: int,
) -> ScoredCandidate:
    default_fill = kwargs.get("default_score", 50)
    axes: dict[ScoringAxis, AxisScore] = {
        ScoringAxis.Novelty: AxisScore(axis=ScoringAxis.Novelty, score=novelty_score, refs=[]),
        ScoringAxis.Patentability: AxisScore(
            axis=ScoringAxis.Patentability, score=feasibility_score, refs=[]
        ),
        ScoringAxis.Inventiveness: AxisScore(
            axis=ScoringAxis.Inventiveness, score=default_fill, refs=[]
        ),
        ScoringAxis.Commercial: AxisScore(axis=ScoringAxis.Commercial, score=default_fill, refs=[]),
        ScoringAxis.Strategic: AxisScore(axis=ScoringAxis.Strategic, score=default_fill, refs=[]),
    }
    return ScoredCandidate(
        candidate_id=CANDIDATE_ID,
        batch_id=BATCH_ID,
        job_id=JOB_ID,
        document_id=DOCUMENT_ID,
        axes=axes,
    )


class TestMaturityClassifier:
    def test_mature_when_both_axes_meet_threshold(self) -> None:
        candidate = _make_candidate(novelty_score=8, feasibility_score=7)

        result = maturity_classifier.classify(candidate, NOVELTY_THRESHOLD, FEASIBILITY_THRESHOLD)

        assert result.maturity == Maturity.Mature

    def test_emerging_when_novelty_below_threshold(self) -> None:
        candidate = _make_candidate(novelty_score=6, feasibility_score=8)

        result = maturity_classifier.classify(candidate, NOVELTY_THRESHOLD, FEASIBILITY_THRESHOLD)

        assert result.maturity == Maturity.Emerging

    def test_emerging_when_feasibility_below_threshold(self) -> None:
        candidate = _make_candidate(novelty_score=8, feasibility_score=5)

        result = maturity_classifier.classify(candidate, NOVELTY_THRESHOLD, FEASIBILITY_THRESHOLD)

        assert result.maturity == Maturity.Emerging

    def test_mature_at_exact_boundary(self) -> None:
        candidate = _make_candidate(novelty_score=7, feasibility_score=6)

        result = maturity_classifier.classify(candidate, NOVELTY_THRESHOLD, FEASIBILITY_THRESHOLD)

        assert result.maturity == Maturity.Mature

    def test_emerging_when_both_below_threshold(self) -> None:
        candidate = _make_candidate(novelty_score=5, feasibility_score=4)

        result = maturity_classifier.classify(candidate, NOVELTY_THRESHOLD, FEASIBILITY_THRESHOLD)

        assert result.maturity == Maturity.Emerging

    def test_reasoning_contains_scores_and_thresholds(self) -> None:
        candidate = _make_candidate(novelty_score=8, feasibility_score=7)

        result = maturity_classifier.classify(candidate, NOVELTY_THRESHOLD, FEASIBILITY_THRESHOLD)

        assert "novelty=8" in result.reasoning
        assert "feasibility=7" in result.reasoning
        assert "threshold" in result.reasoning

    def test_result_id_is_batch_candidate_composite(self) -> None:
        candidate = _make_candidate(novelty_score=8, feasibility_score=7)

        result = maturity_classifier.classify(candidate, NOVELTY_THRESHOLD, FEASIBILITY_THRESHOLD)

        assert result.id == f"{BATCH_ID}:{CANDIDATE_ID}"

    def test_missing_novelty_axis_raises_axis_missing_error(self) -> None:
        axes: dict[ScoringAxis, AxisScore] = {
            ScoringAxis.Patentability: AxisScore(axis=ScoringAxis.Patentability, score=70, refs=[]),
        }
        candidate = ScoredCandidate(
            candidate_id=CANDIDATE_ID,
            batch_id=BATCH_ID,
            job_id=JOB_ID,
            document_id=DOCUMENT_ID,
            axes=axes,
        )

        with pytest.raises(AxisMissingError):
            maturity_classifier.classify(candidate, NOVELTY_THRESHOLD, FEASIBILITY_THRESHOLD)

    def test_missing_patentability_axis_raises_axis_missing_error(self) -> None:
        axes: dict[ScoringAxis, AxisScore] = {
            ScoringAxis.Novelty: AxisScore(axis=ScoringAxis.Novelty, score=80, refs=[]),
        }
        candidate = ScoredCandidate(
            candidate_id=CANDIDATE_ID,
            batch_id=BATCH_ID,
            job_id=JOB_ID,
            document_id=DOCUMENT_ID,
            axes=axes,
        )

        with pytest.raises(AxisMissingError):
            maturity_classifier.classify(candidate, NOVELTY_THRESHOLD, FEASIBILITY_THRESHOLD)
