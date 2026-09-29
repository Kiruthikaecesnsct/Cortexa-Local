import asyncio
import logging
from datetime import UTC, datetime
from unittest.mock import AsyncMock

import pytest

from evidence.application.handlers.process_evidence_request_handler import (
    ProcessEvidenceRequestDeps,
    ProcessEvidenceRequestHandler,
    ProcessOutcome,
    _classify_error,
    _format_exc,
)
from evidence.domain.enums.confidence_band import ConfidenceBand
from evidence.domain.enums.evidence_source import EvidenceSource
from evidence.domain.errors.evidence_errors import (
    CorpusSearchError,
    EvidenceBundleSourceUnavailableError,
    EvidenceCandidateDeadlineExceededError,
    LlmResearchError,
    PatentApiError,
)
from evidence.domain.events.event_envelope import EventEnvelope
from evidence.domain.models.evidence_bundle import EvidenceBundle
from evidence.infrastructure.config.settings import EvidenceSettings

_BATCH_ID = "batch-001"
_DOCUMENT_ID = "doc-001"
_CANDIDATE_ID = "cand-001"
_CORRELATION_ID = "corr-001"


def _make_envelope(
    batch_id: str = _BATCH_ID,
    document_id: str = _DOCUMENT_ID,
    payload: dict | None = None,
) -> EventEnvelope:
    return EventEnvelope(
        batch_id=batch_id,
        document_id=document_id,
        correlation_id=_CORRELATION_ID,
        event_type="extraction.completed",
        schema_version="1.0",
        timestamp="2024-01-01T00:00:00Z",
        payload=payload or {"candidate_id": _CANDIDATE_ID},
    )


def _make_bundle(candidate_id: str = _CANDIDATE_ID) -> EvidenceBundle:
    return EvidenceBundle(
        id="bundle-001",
        batch_id=_BATCH_ID,
        job_id=_BATCH_ID,
        candidate_id=candidate_id,
        document_id=_DOCUMENT_ID,
        hits=[],
        confidence_band=ConfidenceBand.High,
        sources_used=[EvidenceSource.PatentApi],
        source_flags={
            EvidenceSource.PatentApi: True,
            EvidenceSource.SeedCorpus: False,
            EvidenceSource.LlmResearch: False,
        },
        merged_at=datetime.now(UTC),
        patent_source_results=[],
        degraded=False,
        degraded_sources=[],
        active_source_count=1,
        meets_minimum_sources=True,
        minimum_active_sources=1,
    )


def _make_candidate_mock():
    candidate = AsyncMock()
    candidate.claim_text = "A method for widget optimization"
    candidate.tech_field = "mechanical engineering"
    return candidate


def _make_deps(
    triangulation_service: AsyncMock | None = None,
    candidate_reader: AsyncMock | None = None,
) -> ProcessEvidenceRequestDeps:
    repository = AsyncMock()
    repository.find_for_candidate.return_value = None

    publisher = AsyncMock()
    publisher.publish.return_value = None

    triangulation = triangulation_service or AsyncMock()
    if triangulation_service is None:
        triangulation.triangulate.return_value = _make_bundle()

    store_handler = AsyncMock()
    store_response = AsyncMock()
    store_response.published = True
    store_handler.handle.return_value = store_response

    candidate_reader = candidate_reader or AsyncMock()
    if candidate_reader.get_by_candidate_id.return_value is None:
        candidate_reader.get_by_candidate_id.return_value = _make_candidate_mock()

    settings = EvidenceSettings(
        model_router_url="http://test-model-router",
        vector_router_url="http://test-vector-router",
        evidence_completed_topic="evidence.completed",
        evidence_failed_topic="evidence.failed",
    )

    return ProcessEvidenceRequestDeps(
        repository=repository,
        publisher=publisher,
        triangulation=triangulation,
        store_handler=store_handler,
        candidate_reader=candidate_reader,
        settings=settings,
    )


@pytest.mark.asyncio
async def test_handle_llm_research_error_permanent_400_publishes_failed_event():
    triangulation = AsyncMock()
    triangulation.triangulate.side_effect = LlmResearchError("Bad request", status_code=400)

    deps = _make_deps(triangulation_service=triangulation)
    handler = ProcessEvidenceRequestHandler(deps)

    envelope = _make_envelope()
    outcome = await handler.handle(envelope)

    assert outcome == ProcessOutcome.PERMANENT
    deps.publisher.publish.assert_awaited_once()
    call_args = deps.publisher.publish.call_args
    assert call_args[0][0] == "evidence.failed"


@pytest.mark.asyncio
async def test_handle_llm_research_error_transient_503_returns_transient():
    triangulation = AsyncMock()
    triangulation.triangulate.side_effect = LlmResearchError("Service unavailable", status_code=503)

    deps = _make_deps(triangulation_service=triangulation)
    handler = ProcessEvidenceRequestHandler(deps)

    envelope = _make_envelope()
    outcome = await handler.handle(envelope)

    assert outcome == ProcessOutcome.TRANSIENT
    call_args_list = deps.publisher.publish.call_args_list
    failed_calls = [c for c in call_args_list if c[0][0] == "evidence.failed"]
    assert len(failed_calls) == 0


@pytest.mark.asyncio
async def test_handle_ai_model_passed_to_triangulate():
    EXPECTED_PRIMARY = "gpt-5.5"

    triangulation = AsyncMock()
    triangulation.triangulate.return_value = _make_bundle()

    deps = _make_deps(triangulation_service=triangulation)
    handler = ProcessEvidenceRequestHandler(deps)

    envelope = _make_envelope(
        payload={
            "candidate_id": _CANDIDATE_ID,
            "ai_model": EXPECTED_PRIMARY,
        }
    )
    await handler.handle(envelope)

    triangulation.triangulate.assert_awaited_once()
    call_kwargs = triangulation.triangulate.call_args.kwargs
    assert call_kwargs["ai_model"] == EXPECTED_PRIMARY
    assert "secondary_model" not in call_kwargs


@pytest.mark.asyncio
async def test_handle_no_ai_model_passes_none():
    triangulation = AsyncMock()
    triangulation.triangulate.return_value = _make_bundle()

    deps = _make_deps(triangulation_service=triangulation)
    handler = ProcessEvidenceRequestHandler(deps)

    envelope = _make_envelope(payload={"candidate_id": _CANDIDATE_ID})
    await handler.handle(envelope)

    triangulation.triangulate.assert_awaited_once()
    call_kwargs = triangulation.triangulate.call_args.kwargs
    assert call_kwargs["ai_model"] is None


@pytest.mark.asyncio
@pytest.mark.parametrize("status_code", [400, 401, 403, 404, 422])
async def test_handle_llm_research_error_all_permanent_codes_return_permanent(status_code: int):
    triangulation = AsyncMock()
    triangulation.triangulate.side_effect = LlmResearchError(
        f"Error {status_code}", status_code=status_code
    )

    deps = _make_deps(triangulation_service=triangulation)
    handler = ProcessEvidenceRequestHandler(deps)

    envelope = _make_envelope()
    outcome = await handler.handle(envelope)

    assert outcome == ProcessOutcome.PERMANENT
    deps.publisher.publish.assert_awaited_once()


@pytest.mark.asyncio
async def test_handle_patent_api_error_permanent_400_publishes_failed_event():
    triangulation = AsyncMock()
    triangulation.triangulate.side_effect = PatentApiError("Bad request", status_code=400)

    deps = _make_deps(triangulation_service=triangulation)
    handler = ProcessEvidenceRequestHandler(deps)

    envelope = _make_envelope()
    outcome = await handler.handle(envelope)

    assert outcome == ProcessOutcome.PERMANENT
    deps.publisher.publish.assert_awaited_once()


@pytest.mark.asyncio
async def test_handle_corpus_search_error_transient_no_status_code():
    triangulation = AsyncMock()
    triangulation.triangulate.side_effect = CorpusSearchError("Corpus unreachable")

    deps = _make_deps(triangulation_service=triangulation)
    handler = ProcessEvidenceRequestHandler(deps)

    envelope = _make_envelope()
    outcome = await handler.handle(envelope)

    assert outcome == ProcessOutcome.TRANSIENT
    call_args_list = deps.publisher.publish.call_args_list
    failed_calls = [c for c in call_args_list if c[0][0] == "evidence.failed"]
    assert len(failed_calls) == 0


# ---------------------------------------------------------------------------
# BUG144: candidate-deadline classification and failure-log regressions
# ---------------------------------------------------------------------------


def test_classify_error_candidate_deadline_exceeded_is_permanent():
    exc = EvidenceCandidateDeadlineExceededError(
        pending_sources=["LlmResearch"], elapsed_seconds=150.3
    )

    assert _classify_error(exc) == ProcessOutcome.PERMANENT


def test_classify_error_bare_timeout_error_is_transient():
    assert _classify_error(TimeoutError()) == ProcessOutcome.TRANSIENT


def test_classify_error_source_unavailable_never_transient():
    exc = EvidenceBundleSourceUnavailableError("All evidence sources returned no results")

    assert _classify_error(exc) != ProcessOutcome.TRANSIENT


@pytest.mark.asyncio
async def test_handle_candidate_deadline_exceeded_publishes_failed_event_as_permanent():
    triangulation = AsyncMock()
    triangulation.triangulate.side_effect = EvidenceCandidateDeadlineExceededError(
        pending_sources=["LlmResearch"], elapsed_seconds=150.4
    )

    deps = _make_deps(triangulation_service=triangulation)
    handler = ProcessEvidenceRequestHandler(deps)

    envelope = _make_envelope()
    outcome = await handler.handle(envelope)

    assert outcome == ProcessOutcome.PERMANENT
    deps.publisher.publish.assert_awaited_once()
    call_args = deps.publisher.publish.call_args
    assert call_args[0][0] == "evidence.failed"


@pytest.mark.asyncio
async def test_handle_failure_log_line_is_never_empty_and_includes_type(caplog):
    triangulation = AsyncMock()
    triangulation.triangulate.side_effect = EvidenceCandidateDeadlineExceededError(
        pending_sources=["LlmResearch"], elapsed_seconds=150.1
    )

    deps = _make_deps(triangulation_service=triangulation)
    handler = ProcessEvidenceRequestHandler(deps)

    with caplog.at_level("ERROR"):
        await handler.handle(_make_envelope())

    error_records = [r for r in caplog.records if r.levelname == "ERROR"]
    assert len(error_records) >= 1
    message = error_records[-1].getMessage()
    assert message != ""
    assert "EvidenceCandidateDeadlineExceededError" in message
    assert "LlmResearch" in message
    assert "150.1" in message


# --- _format_exc unit tests ---


def test_format_exc_bare_cancelled_error_is_non_empty():
    exc = asyncio.CancelledError()
    result = _format_exc(exc)
    assert result.startswith("CancelledError:")
    assert len(result) > len("CancelledError:")


def test_format_exc_bare_timeout_error_is_non_empty():
    exc = TimeoutError()
    result = _format_exc(exc)
    assert result.startswith("TimeoutError:")
    assert len(result) > len("TimeoutError:")


def test_format_exc_includes_cause_chain():
    root = ConnectionError("upstream refused")
    exc = TimeoutError()
    exc.__cause__ = root
    result = _format_exc(exc)
    assert "TimeoutError" in result
    assert "caused by" in result
    assert "ConnectionError" in result


def test_format_exc_exception_with_message():
    exc = LlmResearchError("model overloaded", status_code=503)
    result = _format_exc(exc)
    assert "LlmResearchError" in result
    assert "503" in result or "model overloaded" in result


# --- logging quality tests ---


@pytest.mark.asyncio
async def test_classify_and_log_empty_message_error_logs_non_empty_error_field(caplog):
    # Simulates cancellation/timeout wrapped into LlmResearchError with empty str()
    root = asyncio.CancelledError()
    wrapped = LlmResearchError("", status_code=None)
    wrapped.__cause__ = root
    triangulation = AsyncMock()
    triangulation.triangulate.side_effect = wrapped

    deps = _make_deps(triangulation_service=triangulation)
    handler = ProcessEvidenceRequestHandler(deps)

    _LOGGER = "evidence.application.handlers.process_evidence_request_handler"
    envelope = _make_envelope()
    with caplog.at_level(logging.ERROR, logger=_LOGGER):
        await handler.handle(envelope)

    error_log = next(
        (r for r in caplog.records if "outcome=" in r.getMessage()),
        None,
    )
    assert error_log is not None
    msg = error_log.getMessage()
    assert "LlmResearchError" in msg
    assert "error= " not in msg


@pytest.mark.asyncio
async def test_classify_and_log_attaches_exc_info_for_sentry(caplog):
    exc = LlmResearchError("", status_code=None)
    triangulation = AsyncMock()
    triangulation.triangulate.side_effect = exc

    deps = _make_deps(triangulation_service=triangulation)
    handler = ProcessEvidenceRequestHandler(deps)

    _LOGGER = "evidence.application.handlers.process_evidence_request_handler"
    envelope = _make_envelope()
    with caplog.at_level(logging.ERROR, logger=_LOGGER):
        await handler.handle(envelope)

    error_log = next(
        (r for r in caplog.records if "outcome=" in r.getMessage()),
        None,
    )
    assert error_log is not None
    assert error_log.exc_info is not None
    assert error_log.exc_info[1] is exc


@pytest.mark.asyncio
async def test_handle_content_filter_400_classified_permanent_no_retry():
    triangulation = AsyncMock()
    triangulation.triangulate.side_effect = LlmResearchError(
        "content_filter: ResponsibleAIPolicyViolation jailbreak detected", status_code=400
    )

    deps = _make_deps(triangulation_service=triangulation)
    handler = ProcessEvidenceRequestHandler(deps)

    envelope = _make_envelope()
    outcome = await handler.handle(envelope)

    assert outcome == ProcessOutcome.PERMANENT
    deps.publisher.publish.assert_awaited_once()
    call_args = deps.publisher.publish.call_args
    assert call_args[0][0] == "evidence.failed"
    event = call_args[0][1]
    assert event.batch_id == _BATCH_ID
    triangulation.triangulate.assert_awaited_once()
