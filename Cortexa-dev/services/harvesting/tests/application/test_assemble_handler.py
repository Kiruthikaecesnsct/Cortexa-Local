import uuid
from unittest.mock import AsyncMock

import pytest

from harvesting.application.dtos.assemble_request import AssembleCandidateDto, AssembleRequestDto
from harvesting.application.dtos.assemble_response import AssembleResponseDto
from harvesting.application.handlers.assemble_handler import AssembleDeps, AssembleHandler
from harvesting.domain.enums.agreement_flag import AgreementFlag
from harvesting.domain.enums.maturity import Maturity
from harvesting.domain.enums.scoring_axis import ScoringAxis
from harvesting.domain.errors.harvesting_errors import (
    AxisMissingError,
    EventPublishError,
    StorageWriteError,
)
from harvesting.domain.models.axis_score import AxisScore
from harvesting.domain.models.citation import Citation, ProvenanceLink

AXIS_SCORE = 50
CANDIDATE_RANK = 1
CANDIDATE_WEIGHTED_SCORE = 75.0


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


def _all_axes() -> dict[ScoringAxis, AxisScore]:
    return {
        ScoringAxis.Novelty: AxisScore(axis=ScoringAxis.Novelty, score=AXIS_SCORE, refs=[]),
        ScoringAxis.Inventiveness: AxisScore(
            axis=ScoringAxis.Inventiveness, score=AXIS_SCORE, refs=[]
        ),
        ScoringAxis.Commercial: AxisScore(axis=ScoringAxis.Commercial, score=AXIS_SCORE, refs=[]),
        ScoringAxis.Strategic: AxisScore(axis=ScoringAxis.Strategic, score=AXIS_SCORE, refs=[]),
        ScoringAxis.Patentability: AxisScore(
            axis=ScoringAxis.Patentability, score=AXIS_SCORE, refs=[]
        ),
    }


def _make_candidate(candidate_id: str = "cand-001", claim_text: str = "") -> AssembleCandidateDto:
    return AssembleCandidateDto(
        candidate_id=candidate_id,
        title="Test Invention",
        description="A novel method for testing.",
        claim_text=claim_text,
        maturity=Maturity.Emerging,
        rank=CANDIDATE_RANK,
        weighted_score=CANDIDATE_WEIGHTED_SCORE,
        axes=_all_axes(),
        agreement_flag=AgreementFlag.Full,
        citations=[_make_citation()],
        provenance_links=[_make_provenance_link()],
    )


def _make_request(candidates: list[AssembleCandidateDto] | None = None) -> AssembleRequestDto:
    return AssembleRequestDto(
        batch_id="batch-abc",
        document_id="doc-xyz",
        correlation_id="corr-123",
        candidates=candidates if candidates is not None else [_make_candidate()],
    )


def _make_handler() -> tuple[AssembleHandler, AsyncMock, AsyncMock]:
    repo = AsyncMock()
    publisher = AsyncMock()
    deps = AssembleDeps(report_repo=repo, publisher=publisher)
    handler = AssembleHandler(deps)
    return handler, repo, publisher


async def test_handle_single_candidate_returns_response_with_uuid_report_id_and_count_of_one():
    handler, repo, publisher = _make_handler()
    request = _make_request()

    result = await handler.handle(request)

    assert isinstance(result, AssembleResponseDto)
    assert result.candidate_count == 1
    parsed = uuid.UUID(result.report_id)
    assert str(parsed) == result.report_id


async def test_handle_single_candidate_calls_repo_save_once():
    handler, repo, publisher = _make_handler()
    request = _make_request()

    await handler.handle(request)

    repo.save.assert_called_once()


async def test_handle_single_candidate_calls_publisher_publish_once():
    handler, repo, publisher = _make_handler()
    request = _make_request()

    await handler.handle(request)

    publisher.publish.assert_called_once()


async def test_handle_candidate_missing_one_axis_raises_axis_missing_error():
    handler, _, _ = _make_handler()
    axes = _all_axes()
    del axes[ScoringAxis.Patentability]
    candidate = AssembleCandidateDto(
        candidate_id="cand-002",
        title="Incomplete Candidate",
        description="Missing one axis.",
        maturity=Maturity.Mature,
        rank=CANDIDATE_RANK,
        weighted_score=CANDIDATE_WEIGHTED_SCORE,
        axes=axes,
        agreement_flag=AgreementFlag.Partial,
        citations=[],
        provenance_links=[],
    )
    request = _make_request(candidates=[candidate])

    with pytest.raises(AxisMissingError):
        await handler.handle(request)


async def test_handle_repo_save_failure_propagates_storage_write_error():
    handler, repo, publisher = _make_handler()
    repo.save.side_effect = StorageWriteError("disk full")
    request = _make_request()

    with pytest.raises(StorageWriteError):
        await handler.handle(request)


async def test_handle_publisher_failure_propagates_event_publish_error_after_save():
    handler, repo, publisher = _make_handler()
    publisher.publish.side_effect = EventPublishError("service bus unavailable")
    request = _make_request()

    with pytest.raises(EventPublishError):
        await handler.handle(request)

    repo.save.assert_called_once()


async def test_handle_three_candidates_returns_candidate_count_of_three():
    handler, _, publisher = _make_handler()
    candidates = [
        _make_candidate("cand-001"),
        _make_candidate("cand-002"),
        _make_candidate("cand-003"),
    ]
    request = _make_request(candidates=candidates)

    result = await handler.handle(request)

    assert result.candidate_count == 3


async def test_handle_three_candidates_calls_publisher_publish_once():
    handler, _, publisher = _make_handler()
    candidates = [
        _make_candidate("cand-001"),
        _make_candidate("cand-002"),
        _make_candidate("cand-003"),
    ]
    request = _make_request(candidates=candidates)

    await handler.handle(request)

    publisher.publish.assert_called_once()


async def test_handle_propagates_claim_text_to_report_candidate():
    handler, repo, _ = _make_handler()
    claim = "A method for optimizing neural network inference."
    candidate = _make_candidate("cand-claim", claim_text=claim)
    request = _make_request(candidates=[candidate])

    await handler.handle(request)

    repo.save.assert_called_once()
    saved_report = repo.save.call_args[0][0]
    assert len(saved_report.candidates) == 1
    assert saved_report.candidates[0].claim_text == claim


async def test_handle_propagates_empty_claim_text_to_report_candidate():
    handler, repo, _ = _make_handler()
    candidate = _make_candidate("cand-no-claim", claim_text="")
    request = _make_request(candidates=[candidate])

    await handler.handle(request)

    repo.save.assert_called_once()
    saved_report = repo.save.call_args[0][0]
    assert len(saved_report.candidates) == 1
    assert saved_report.candidates[0].claim_text == ""


async def test_handle_propagates_claim_draft_to_report_candidate():
    handler, repo, _ = _make_handler()
    drafted_claim = "A method comprising: steps for achieving X; steps for achieving Y."
    candidate = _make_candidate("cand-draft", claim_text="Original claim.")
    candidate.claim_draft = drafted_claim
    request = _make_request(candidates=[candidate])

    await handler.handle(request)

    repo.save.assert_called_once()
    saved_report = repo.save.call_args[0][0]
    assert len(saved_report.candidates) == 1
    assert saved_report.candidates[0].claim_draft == drafted_claim
