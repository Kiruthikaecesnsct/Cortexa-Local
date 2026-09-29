import pytest

from harvesting.domain.enums.scoring_axis import ScoringAxis
from harvesting.domain.errors.harvesting_errors import AxisMissingError
from harvesting.domain.models.axis_score import AxisScore
from harvesting.domain.models.rank_weights import RankWeights
from harvesting.domain.models.scored_candidate import ScoredCandidate
from harvesting.domain.services import harvesting_ranker

DEFAULT_WEIGHTS = RankWeights(novelty=0.4, feasibility=0.25, strategic=0.2, patentability=0.15)

CANDIDATE_ID = "cand-001"
BATCH_ID = "batch-abc"
JOB_ID = "job-xyz"
DOCUMENT_ID = "doc-001"

NOVELTY_10 = 10
FEASIBILITY_8 = 8
STRATEGIC_7 = 7
PATENTABILITY_9 = 9
EXPECTED_WEIGHTED_SCORE = 8.75

CUSTOM_WEIGHTS = RankWeights(novelty=0.5, feasibility=0.25, strategic=0.15, patentability=0.10)
CUSTOM_NOVELTY = 80
CUSTOM_FEASIBILITY = 60
CUSTOM_STRATEGIC = 40
CUSTOM_PATENTABILITY = 20
EXPECTED_CUSTOM_SCORE = (
    CUSTOM_NOVELTY * 0.5
    + CUSTOM_FEASIBILITY * 0.25
    + CUSTOM_STRATEGIC * 0.15
    + CUSTOM_PATENTABILITY * 0.10
)


def _make_candidate(
    novelty: int,
    feasibility: int,
    strategic: int,
    patentability: int,
    candidate_id: str = CANDIDATE_ID,
) -> ScoredCandidate:
    return ScoredCandidate(
        candidate_id=candidate_id,
        batch_id=BATCH_ID,
        job_id=JOB_ID,
        document_id=DOCUMENT_ID,
        axes={
            ScoringAxis.Novelty: AxisScore(axis=ScoringAxis.Novelty, score=novelty, refs=[]),
            ScoringAxis.Inventiveness: AxisScore(
                axis=ScoringAxis.Inventiveness, score=feasibility, refs=[]
            ),
            ScoringAxis.Strategic: AxisScore(axis=ScoringAxis.Strategic, score=strategic, refs=[]),
            ScoringAxis.Patentability: AxisScore(
                axis=ScoringAxis.Patentability, score=patentability, refs=[]
            ),
        },
    )


class TestWeightFormula:
    def test_rank_applies_correct_weighted_formula(self) -> None:
        candidate = _make_candidate(
            novelty=NOVELTY_10,
            feasibility=FEASIBILITY_8,
            strategic=STRATEGIC_7,
            patentability=PATENTABILITY_9,
        )

        result = harvesting_ranker.rank([candidate], DEFAULT_WEIGHTS)

        assert result[0].weighted_score == pytest.approx(EXPECTED_WEIGHTED_SCORE)

    def test_rank_applies_custom_weights(self) -> None:
        candidate = _make_candidate(
            novelty=CUSTOM_NOVELTY,
            feasibility=CUSTOM_FEASIBILITY,
            strategic=CUSTOM_STRATEGIC,
            patentability=CUSTOM_PATENTABILITY,
        )

        result = harvesting_ranker.rank([candidate], CUSTOM_WEIGHTS)

        assert result[0].weighted_score == pytest.approx(EXPECTED_CUSTOM_SCORE)


class TestRankOrdering:
    def test_rank_orders_candidates_descending_by_weighted_score(self) -> None:
        low = _make_candidate(
            novelty=72, feasibility=72, strategic=72, patentability=72, candidate_id="cand-low"
        )
        mid = _make_candidate(
            novelty=85, feasibility=85, strategic=85, patentability=85, candidate_id="cand-mid"
        )
        high = _make_candidate(
            novelty=91, feasibility=91, strategic=91, patentability=91, candidate_id="cand-high"
        )

        result = harvesting_ranker.rank([low, mid, high], DEFAULT_WEIGHTS)

        assert [r.candidate_id for r in result] == ["cand-high", "cand-mid", "cand-low"]

    def test_rank_assigns_positions_starting_at_one(self) -> None:
        low = _make_candidate(
            novelty=72, feasibility=72, strategic=72, patentability=72, candidate_id="cand-low"
        )
        mid = _make_candidate(
            novelty=85, feasibility=85, strategic=85, patentability=85, candidate_id="cand-mid"
        )
        high = _make_candidate(
            novelty=91, feasibility=91, strategic=91, patentability=91, candidate_id="cand-high"
        )

        result = harvesting_ranker.rank([low, mid, high], DEFAULT_WEIGHTS)

        assert [r.rank for r in result] == [1, 2, 3]

    def test_single_candidate_gets_rank_one(self) -> None:
        candidate = _make_candidate(novelty=50, feasibility=50, strategic=50, patentability=50)

        result = harvesting_ranker.rank([candidate], DEFAULT_WEIGHTS)

        assert result[0].rank == 1

    def test_empty_candidate_list_returns_empty_list(self) -> None:
        result = harvesting_ranker.rank([], DEFAULT_WEIGHTS)

        assert result == []


class TestAxisScoresInResult:
    def test_all_four_axis_scores_are_present_in_result(self) -> None:
        candidate = _make_candidate(
            novelty=NOVELTY_10,
            feasibility=FEASIBILITY_8,
            strategic=STRATEGIC_7,
            patentability=PATENTABILITY_9,
        )

        result = harvesting_ranker.rank([candidate], DEFAULT_WEIGHTS)
        ranked = result[0]

        assert ranked.novelty_score == NOVELTY_10
        assert ranked.feasibility_score == FEASIBILITY_8
        assert ranked.strategic_score == STRATEGIC_7
        assert ranked.patentability_score == PATENTABILITY_9


class TestRationale:
    def test_rationale_contains_all_axis_labels(self) -> None:
        candidate = _make_candidate(
            novelty=NOVELTY_10,
            feasibility=FEASIBILITY_8,
            strategic=STRATEGIC_7,
            patentability=PATENTABILITY_9,
        )

        result = harvesting_ranker.rank([candidate], DEFAULT_WEIGHTS)
        rationale = result[0].rationale

        assert "Novelty" in rationale
        assert "Feasibility" in rationale
        assert "Strategic" in rationale
        assert "Patentability" in rationale

    def test_rationale_contains_weight_percentages(self) -> None:
        candidate = _make_candidate(
            novelty=NOVELTY_10,
            feasibility=FEASIBILITY_8,
            strategic=STRATEGIC_7,
            patentability=PATENTABILITY_9,
        )

        result = harvesting_ranker.rank([candidate], DEFAULT_WEIGHTS)
        rationale = result[0].rationale

        assert "% weight" in rationale


class TestMissingAxis:
    def test_missing_novelty_axis_raises_axis_missing_error(self) -> None:
        candidate = ScoredCandidate(
            candidate_id=CANDIDATE_ID,
            batch_id=BATCH_ID,
            job_id=JOB_ID,
            document_id=DOCUMENT_ID,
            axes={
                ScoringAxis.Inventiveness: AxisScore(
                    axis=ScoringAxis.Inventiveness, score=50, refs=[]
                ),
                ScoringAxis.Strategic: AxisScore(axis=ScoringAxis.Strategic, score=50, refs=[]),
                ScoringAxis.Patentability: AxisScore(
                    axis=ScoringAxis.Patentability, score=50, refs=[]
                ),
            },
        )

        with pytest.raises(AxisMissingError):
            harvesting_ranker.rank([candidate], DEFAULT_WEIGHTS)

    def test_missing_inventiveness_axis_raises_axis_missing_error(self) -> None:
        candidate = ScoredCandidate(
            candidate_id=CANDIDATE_ID,
            batch_id=BATCH_ID,
            job_id=JOB_ID,
            document_id=DOCUMENT_ID,
            axes={
                ScoringAxis.Novelty: AxisScore(axis=ScoringAxis.Novelty, score=50, refs=[]),
                ScoringAxis.Strategic: AxisScore(axis=ScoringAxis.Strategic, score=50, refs=[]),
                ScoringAxis.Patentability: AxisScore(
                    axis=ScoringAxis.Patentability, score=50, refs=[]
                ),
            },
        )

        with pytest.raises(AxisMissingError):
            harvesting_ranker.rank([candidate], DEFAULT_WEIGHTS)
