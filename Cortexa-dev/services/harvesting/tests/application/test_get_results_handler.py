from datetime import UTC, datetime
from unittest.mock import AsyncMock

import pytest

from harvesting.application.dtos.harvesting_result_response import (
    CandidateDto,
    HarvestingResultResponseDto,
    VerdictDto,
)
from harvesting.application.handlers.get_results_handler import GetResultsDeps, GetResultsHandler
from harvesting.domain.enums.agreement_flag import AgreementFlag
from harvesting.domain.enums.maturity import Maturity
from harvesting.domain.enums.scoring_axis import ScoringAxis
from harvesting.domain.errors.harvesting_errors import ReportNotFoundError
from harvesting.domain.models.axis_score import AxisScore
from harvesting.domain.models.citation import Citation, ProvenanceLink
from harvesting.domain.models.harvesting_report import HarvestingReport
from harvesting.domain.models.report_candidate import ReportCandidate

BATCH_ID = "batch-abc"
DOCUMENT_ID = "doc-xyz"
REPORT_ID = "report-001"
CANDIDATE_ID_1 = "cand-001"
CANDIDATE_ID_2 = "cand-002"
TITLE_1 = "Test Invention One"
TITLE_2 = "Test Invention Two"
DESCRIPTION_1 = "A novel method for testing."
DESCRIPTION_2 = "Another breakthrough invention."
WEIGHTED_SCORE = 75.0
RANK_1 = 1
RANK_2 = 2
AXIS_SCORE_VALUE = 50

SCORE_PURSUE = 80.0
SCORE_INVESTIGATE = 50.0
SCORE_ABANDON = 20.0


def _make_citation(ref: str = "E1") -> Citation:
    return Citation(
        ref=ref,
        source_type="patent_api",
        title="Prior art patent",
        patent_id="US1234567A",
        url="https://example.com/patent/1",
        similarity=0.87,
    )


def _make_provenance_link(document_id: str = "doc-001") -> ProvenanceLink:
    return ProvenanceLink(
        document_id=document_id,
        locator="chunk-001",
        source_kind="extraction",
    )


def _make_axis_scores() -> dict[ScoringAxis, AxisScore]:
    return {
        ScoringAxis.Novelty: AxisScore(axis=ScoringAxis.Novelty, score=AXIS_SCORE_VALUE, refs=[]),
        ScoringAxis.Inventiveness: AxisScore(
            axis=ScoringAxis.Inventiveness, score=AXIS_SCORE_VALUE, refs=[]
        ),
        ScoringAxis.Commercial: AxisScore(
            axis=ScoringAxis.Commercial, score=AXIS_SCORE_VALUE, refs=[]
        ),
        ScoringAxis.Strategic: AxisScore(
            axis=ScoringAxis.Strategic, score=AXIS_SCORE_VALUE, refs=[]
        ),
        ScoringAxis.Patentability: AxisScore(
            axis=ScoringAxis.Patentability, score=AXIS_SCORE_VALUE, refs=[]
        ),
    }


def _make_report_candidate(
    candidate_id: str = CANDIDATE_ID_1,
    title: str = TITLE_1,
    description: str = DESCRIPTION_1,
    rank: int = RANK_1,
) -> ReportCandidate:
    return ReportCandidate(
        candidate_id=candidate_id,
        title=title,
        description=description,
        maturity=Maturity.Emerging,
        rank=rank,
        weighted_score=WEIGHTED_SCORE,
        axes=_make_axis_scores(),
        agreement_flag=AgreementFlag.Full,
        citations=[_make_citation()],
        provenance_links=[_make_provenance_link()],
    )


def _make_report(candidates: list[ReportCandidate] | None = None) -> HarvestingReport:
    return HarvestingReport(
        id=REPORT_ID,
        batch_id=BATCH_ID,
        document_id=DOCUMENT_ID,
        generated_at=datetime(2026, 6, 27, 10, 0, 0, tzinfo=UTC),
        candidates=candidates if candidates is not None else [_make_report_candidate()],
    )


def _make_verdict_dict(
    candidate_id: str = CANDIDATE_ID_1, composite_score: float = SCORE_PURSUE
) -> dict:
    return {
        "id": f"verdict-{candidate_id}",
        "candidate_id": candidate_id,
        "composite_score": composite_score,
        "axes": {
            "Novelty": {"score": 80},
            "Inventiveness": {"score": 75},
        },
        "batch_id": BATCH_ID,
    }


def _make_handler() -> tuple[GetResultsHandler, AsyncMock, AsyncMock]:
    report_repo = AsyncMock()
    verdicts_repo = AsyncMock()
    deps = GetResultsDeps(report_repo=report_repo, verdicts_repo=verdicts_repo)
    handler = GetResultsHandler(deps)
    return handler, report_repo, verdicts_repo


async def test_handle_happy_path_returns_response_with_correct_batch_id():
    handler, report_repo, verdicts_repo = _make_handler()
    report = _make_report()
    verdicts = [_make_verdict_dict()]
    report_repo.get_by_batch.return_value = report
    verdicts_repo.get_by_batch.return_value = verdicts

    result = await handler.handle(BATCH_ID)

    assert isinstance(result, HarvestingResultResponseDto)
    assert result.batch_id == BATCH_ID
    assert result.id == REPORT_ID


async def test_handle_happy_path_maps_report_candidate_to_candidate_dto():
    handler, report_repo, verdicts_repo = _make_handler()
    report = _make_report()
    verdicts = [_make_verdict_dict()]
    report_repo.get_by_batch.return_value = report
    verdicts_repo.get_by_batch.return_value = verdicts

    result = await handler.handle(BATCH_ID)

    assert len(result.candidates) == 1
    candidate = result.candidates[0]
    assert isinstance(candidate, CandidateDto)
    assert candidate.id == CANDIDATE_ID_1
    assert candidate.title == TITLE_1
    assert candidate.abstract == DESCRIPTION_1
    assert candidate.source_asset_id == DOCUMENT_ID
    assert candidate.batch_id == BATCH_ID


async def test_handle_happy_path_maps_verdict_dict_to_verdict_dto():
    handler, report_repo, verdicts_repo = _make_handler()
    report = _make_report()
    verdicts = [_make_verdict_dict(composite_score=SCORE_PURSUE)]
    report_repo.get_by_batch.return_value = report
    verdicts_repo.get_by_batch.return_value = verdicts

    result = await handler.handle(BATCH_ID)

    assert len(result.verdicts) == 1
    verdict = result.verdicts[0]
    assert isinstance(verdict, VerdictDto)
    assert verdict.candidate_id == CANDIDATE_ID_1
    assert verdict.patentability_score == SCORE_PURSUE
    assert verdict.recommendation == "pursue"
    assert verdict.batch_id == BATCH_ID


async def test_handle_missing_report_raises_report_not_found_error():
    handler, report_repo, verdicts_repo = _make_handler()
    report_repo.get_by_batch.side_effect = ReportNotFoundError(BATCH_ID)

    with pytest.raises(ReportNotFoundError):
        await handler.handle(BATCH_ID)


async def test_handle_score_pursue_maps_to_pursue_recommendation():
    handler, report_repo, verdicts_repo = _make_handler()
    report = _make_report()
    verdicts = [_make_verdict_dict(composite_score=SCORE_PURSUE)]
    report_repo.get_by_batch.return_value = report
    verdicts_repo.get_by_batch.return_value = verdicts

    result = await handler.handle(BATCH_ID)

    assert result.verdicts[0].recommendation == "pursue"


async def test_handle_score_investigate_maps_to_investigate_recommendation():
    handler, report_repo, verdicts_repo = _make_handler()
    report = _make_report()
    verdicts = [_make_verdict_dict(composite_score=SCORE_INVESTIGATE)]
    report_repo.get_by_batch.return_value = report
    verdicts_repo.get_by_batch.return_value = verdicts

    result = await handler.handle(BATCH_ID)

    assert result.verdicts[0].recommendation == "investigate"


async def test_handle_score_abandon_maps_to_abandon_recommendation():
    handler, report_repo, verdicts_repo = _make_handler()
    report = _make_report()
    verdicts = [_make_verdict_dict(composite_score=SCORE_ABANDON)]
    report_repo.get_by_batch.return_value = report
    verdicts_repo.get_by_batch.return_value = verdicts

    result = await handler.handle(BATCH_ID)

    assert result.verdicts[0].recommendation == "abandon"


async def test_handle_builds_summary_with_correct_counts():
    handler, report_repo, verdicts_repo = _make_handler()
    candidates = [
        _make_report_candidate(CANDIDATE_ID_1, TITLE_1, DESCRIPTION_1, RANK_1),
        _make_report_candidate(CANDIDATE_ID_2, TITLE_2, DESCRIPTION_2, RANK_2),
    ]
    report = _make_report(candidates=candidates)
    verdicts = [
        _make_verdict_dict(CANDIDATE_ID_1, SCORE_PURSUE),
        _make_verdict_dict(CANDIDATE_ID_2, SCORE_INVESTIGATE),
    ]
    report_repo.get_by_batch.return_value = report
    verdicts_repo.get_by_batch.return_value = verdicts

    result = await handler.handle(BATCH_ID)

    assert result.summary == "Found 2 candidate(s): 1 to pursue, 1 to investigate, 0 to abandon"


async def test_handle_summary_counts_all_three_recommendation_types():
    handler, report_repo, verdicts_repo = _make_handler()
    candidates = [
        _make_report_candidate("cand-001", "Title 1", "Desc 1", 1),
        _make_report_candidate("cand-002", "Title 2", "Desc 2", 2),
        _make_report_candidate("cand-003", "Title 3", "Desc 3", 3),
    ]
    report = _make_report(candidates=candidates)
    verdicts = [
        _make_verdict_dict("cand-001", SCORE_PURSUE),
        _make_verdict_dict("cand-002", SCORE_INVESTIGATE),
        _make_verdict_dict("cand-003", SCORE_ABANDON),
    ]
    report_repo.get_by_batch.return_value = report
    verdicts_repo.get_by_batch.return_value = verdicts

    result = await handler.handle(BATCH_ID)

    assert result.summary == "Found 3 candidate(s): 1 to pursue, 1 to investigate, 1 to abandon"


async def test_handle_empty_candidates_returns_no_candidates_summary():
    handler, report_repo, verdicts_repo = _make_handler()
    report = _make_report(candidates=[])
    report_repo.get_by_batch.return_value = report
    verdicts_repo.get_by_batch.return_value = []

    result = await handler.handle(BATCH_ID)

    assert result.summary == "No candidates found"
    assert len(result.candidates) == 0
    assert len(result.verdicts) == 0


async def test_handle_verdict_missing_composite_score_defaults_to_zero():
    handler, report_repo, verdicts_repo = _make_handler()
    report = _make_report()
    verdict_dict = {
        "id": "verdict-001",
        "candidate_id": CANDIDATE_ID_1,
        "batch_id": BATCH_ID,
    }
    report_repo.get_by_batch.return_value = report
    verdicts_repo.get_by_batch.return_value = [verdict_dict]

    result = await handler.handle(BATCH_ID)

    assert result.verdicts[0].patentability_score == 0.0
    assert result.verdicts[0].recommendation == "abandon"


async def test_handle_builds_rationale_from_verdict_axes():
    handler, report_repo, verdicts_repo = _make_handler()
    report = _make_report()
    verdict_dict = {
        "id": "verdict-001",
        "candidate_id": CANDIDATE_ID_1,
        "composite_score": SCORE_PURSUE,
        "axes": {
            "Novelty": {"score": 85},
            "Patentability": {"score": 90},
        },
        "batch_id": BATCH_ID,
    }
    report_repo.get_by_batch.return_value = report
    verdicts_repo.get_by_batch.return_value = [verdict_dict]

    result = await handler.handle(BATCH_ID)

    rationale = result.verdicts[0].rationale
    assert "Novelty: 85" in rationale
    assert "Patentability: 90" in rationale


async def test_handle_verdict_without_axes_returns_default_rationale():
    handler, report_repo, verdicts_repo = _make_handler()
    report = _make_report()
    verdict_dict = {
        "id": "verdict-001",
        "candidate_id": CANDIDATE_ID_1,
        "composite_score": SCORE_PURSUE,
        "batch_id": BATCH_ID,
    }
    report_repo.get_by_batch.return_value = report
    verdicts_repo.get_by_batch.return_value = [verdict_dict]

    result = await handler.handle(BATCH_ID)

    assert result.verdicts[0].rationale == "Assessed based on composite scoring"


async def test_handle_enriches_candidate_with_ranked_fields():
    handler, report_repo, verdicts_repo = _make_handler()
    candidate = ReportCandidate(
        candidate_id="cand-rich-001",
        title="Rich Candidate",
        description="A fully-ranked candidate with all fields",
        maturity=Maturity.Mature,
        rank=1,
        weighted_score=72.5,
        axes={
            ScoringAxis.Novelty: AxisScore(
                axis=ScoringAxis.Novelty, score=80, refs=["ref-novelty-1"]
            ),
            ScoringAxis.Inventiveness: AxisScore(
                axis=ScoringAxis.Inventiveness, score=70, refs=["ref-inv-1"]
            ),
            ScoringAxis.Commercial: AxisScore(axis=ScoringAxis.Commercial, score=75, refs=[]),
            ScoringAxis.Strategic: AxisScore(
                axis=ScoringAxis.Strategic, score=68, refs=["ref-strat-1", "ref-strat-2"]
            ),
            ScoringAxis.Patentability: AxisScore(
                axis=ScoringAxis.Patentability, score=90, refs=["ref-pat-1"]
            ),
        },
        agreement_flag=AgreementFlag.Full,
        citations=[
            _make_citation("E1"),
            Citation(
                ref="E2",
                source_type="vector_corpus",
                title="Related prior art",
                patent_id="US7654321B",
                url="https://example.com/patent/456",
                similarity=0.62,
            ),
        ],
        provenance_links=[
            _make_provenance_link("doc-rich-001"),
            ProvenanceLink(
                document_id="doc-rich-002", locator="chunk-002", source_kind="ingestion"
            ),
        ],
    )
    report = _make_report(candidates=[candidate])
    verdicts = []
    report_repo.get_by_batch.return_value = report
    verdicts_repo.get_by_batch.return_value = verdicts

    result = await handler.handle(BATCH_ID)

    assert len(result.candidates) == 1
    c = result.candidates[0]
    assert c.candidate_id == "cand-rich-001"
    assert c.title == "Rich Candidate"
    assert c.description == "A fully-ranked candidate with all fields"
    assert c.maturity == "Mature"
    assert c.rank == 1
    assert c.weighted_score == 72.5
    assert len(c.axes) == 5
    assert c.axes["Novelty"].axis == "Novelty"
    assert c.axes["Novelty"].score == 80
    assert c.axes["Novelty"].refs == ["ref-novelty-1"]
    assert c.axes["Patentability"].score == 90
    assert c.agreement_flag == "full"
    assert len(c.citations) == 2
    assert c.citations[0].ref == "E1"
    assert c.citations[0].url == "https://example.com/patent/1"
    assert c.citations[1].patent_id == "US7654321B"
    assert len(c.provenance_links) == 2
    assert c.provenance_links[0].document_id == "doc-rich-001"
    assert c.provenance_links[1].locator == "chunk-002"


async def test_claim_draft_returns_claim_text_when_present():
    handler, report_repo, verdicts_repo = _make_handler()
    claim = "A method for processing data using distributed consensus."
    candidate = ReportCandidate(
        candidate_id="cand-claim",
        title="Claim Test",
        description="Description here",
        claim_text="",
        claim_draft=claim,
        maturity=Maturity.Emerging,
        rank=1,
        weighted_score=60.0,
        axes=_make_axis_scores(),
        agreement_flag=AgreementFlag.Full,
        citations=[],
        provenance_links=[],
    )
    report = _make_report(candidates=[candidate])
    report_repo.get_by_batch.return_value = report
    verdicts_repo.get_by_batch.return_value = []

    result = await handler.handle(BATCH_ID)

    assert len(result.candidates) == 1
    assert result.candidates[0].claim_draft == claim


async def test_claim_draft_returns_empty_when_claim_text_empty():
    handler, report_repo, verdicts_repo = _make_handler()
    candidate = ReportCandidate(
        candidate_id="cand-no-claim",
        title="No Claim",
        description="Just a description",
        claim_text="",
        maturity=Maturity.Emerging,
        rank=1,
        weighted_score=50.0,
        axes=_make_axis_scores(),
        agreement_flag=AgreementFlag.Full,
        citations=[],
        provenance_links=[],
    )
    report = _make_report(candidates=[candidate])
    report_repo.get_by_batch.return_value = report
    verdicts_repo.get_by_batch.return_value = []

    result = await handler.handle(BATCH_ID)

    assert len(result.candidates) == 1
    assert result.candidates[0].claim_draft == ""


async def test_claim_draft_strips_whitespace():
    handler, report_repo, verdicts_repo = _make_handler()
    candidate = ReportCandidate(
        candidate_id="cand-ws",
        title="Whitespace Test",
        description="Testing whitespace handling",
        claim_text="",
        claim_draft="  A system for optimizing resource allocation.  ",
        maturity=Maturity.Emerging,
        rank=1,
        weighted_score=55.0,
        axes=_make_axis_scores(),
        agreement_flag=AgreementFlag.Full,
        citations=[],
        provenance_links=[],
    )
    report = _make_report(candidates=[candidate])
    report_repo.get_by_batch.return_value = report
    verdicts_repo.get_by_batch.return_value = []

    result = await handler.handle(BATCH_ID)

    assert len(result.candidates) == 1
    assert result.candidates[0].claim_draft == "A system for optimizing resource allocation."


async def test_handle_maps_source_availability_and_status_from_report_candidate():
    handler, report_repo, verdicts_repo = _make_handler()
    source_availability = {
        "patent_apis": True,
        "vector_corpus": True,
        "llm_deep_research": False,
    }
    source_status = {
        "patent_apis": "active",
        "vector_corpus": "active",
        "llm_deep_research": "timeout",
    }
    candidate = ReportCandidate(
        candidate_id="cand-source-status",
        title="Source Status Test",
        description="Candidate with one failed evidence source",
        maturity=Maturity.Emerging,
        rank=1,
        weighted_score=65.0,
        axes=_make_axis_scores(),
        agreement_flag=AgreementFlag.Full,
        citations=[],
        provenance_links=[],
        source_availability=source_availability,
        source_status=source_status,
    )
    report = _make_report(candidates=[candidate])
    report_repo.get_by_batch.return_value = report
    verdicts_repo.get_by_batch.return_value = []

    result = await handler.handle(BATCH_ID)

    assert len(result.candidates) == 1
    mapped = result.candidates[0]
    assert mapped.source_availability == source_availability
    assert mapped.source_status == source_status
    assert mapped.source_status["llm_deep_research"] == "timeout"


async def test_handle_candidate_without_source_data_serializes_empty_dicts():
    handler, report_repo, verdicts_repo = _make_handler()
    candidate = _make_report_candidate()
    report = _make_report(candidates=[candidate])
    report_repo.get_by_batch.return_value = report
    verdicts_repo.get_by_batch.return_value = []

    result = await handler.handle(BATCH_ID)

    assert len(result.candidates) == 1
    mapped = result.candidates[0]
    assert mapped.source_availability == {}
    assert mapped.source_status == {}
