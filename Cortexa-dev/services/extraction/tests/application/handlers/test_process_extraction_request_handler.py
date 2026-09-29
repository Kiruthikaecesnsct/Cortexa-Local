from unittest.mock import AsyncMock

import pytest

from extraction.application.handlers.process_extraction_request_handler import (
    ProcessExtractionDeps,
    ProcessExtractionRequestHandler,
    ProcessOutcome,
)
from extraction.domain.errors.extraction_errors import ModelCallFailed
from extraction.domain.events.event_envelope import EventEnvelope

_BATCH_ID = "batch-001"
_DOCUMENT_ID = "doc-001"
_CORRELATION_ID = "corr-001"
_PERMANENT_STATUS_CODES = {400, 401, 403, 404, 422}


def _make_envelope(
    batch_id: str = _BATCH_ID,
    document_id: str | None = _DOCUMENT_ID,
    payload: dict | None = None,
) -> EventEnvelope:
    return EventEnvelope(
        batch_id=batch_id,
        document_id=document_id,
        correlation_id=_CORRELATION_ID,
        event_type="ingestion.completed",
        schema_version="1.0",
        timestamp="2024-01-01T00:00:00Z",
        payload=payload or {},
    )


def _make_deps(extract_handler: AsyncMock | None = None) -> ProcessExtractionDeps:
    candidate_repo = AsyncMock()
    candidate_repo.exists_for_unit.return_value = False

    document_reader = AsyncMock()
    document_reader.get_source_meta.return_value = ("paper", "test.pdf")

    chunk_reader = AsyncMock()
    chunk_reader.get_chunks.return_value = [
        AsyncMock(
            text="chunk one",
            order_index=0,
            source_span=AsyncMock(),
            document_id=_DOCUMENT_ID,
        )
    ]

    extract_handler = extract_handler or AsyncMock()
    extract_handler.handle.return_value = []

    store_handler = AsyncMock()
    store_handler.handle.return_value = None

    event_publisher = AsyncMock()
    event_publisher.publish.return_value = None

    return ProcessExtractionDeps(
        candidate_repo=candidate_repo,
        document_reader=document_reader,
        chunk_reader=chunk_reader,
        extract_handler=extract_handler,
        store_handler=store_handler,
        event_publisher=event_publisher,
        extraction_failed_topic="extraction.failed",
        concurrency=1,
    )


@pytest.mark.asyncio
async def test_handle_model_call_failed_permanent_400_publishes_failed_event():
    extract_handler = AsyncMock()
    extract_handler.handle.side_effect = ModelCallFailed("Bad request", status_code=400)

    deps = _make_deps(extract_handler=extract_handler)
    handler = ProcessExtractionRequestHandler(deps)

    envelope = _make_envelope()
    outcome = await handler.handle(envelope)

    assert outcome == ProcessOutcome.PERMANENT
    deps.event_publisher.publish.assert_awaited_once()
    call_args = deps.event_publisher.publish.call_args
    assert call_args[0][0] == "extraction.failed"


@pytest.mark.asyncio
async def test_handle_model_call_failed_transient_status_none_no_event():
    extract_handler = AsyncMock()
    extract_handler.handle.side_effect = ModelCallFailed("Retries exhausted", status_code=None)

    deps = _make_deps(extract_handler=extract_handler)
    handler = ProcessExtractionRequestHandler(deps)

    envelope = _make_envelope()
    outcome = await handler.handle(envelope)

    assert outcome == ProcessOutcome.TRANSIENT
    deps.event_publisher.publish.assert_not_awaited()


@pytest.mark.asyncio
async def test_handle_model_call_failed_transient_5xx_no_event():
    extract_handler = AsyncMock()
    extract_handler.handle.side_effect = ModelCallFailed("Service unavailable", status_code=503)

    deps = _make_deps(extract_handler=extract_handler)
    handler = ProcessExtractionRequestHandler(deps)

    envelope = _make_envelope()
    outcome = await handler.handle(envelope)

    assert outcome == ProcessOutcome.TRANSIENT
    deps.event_publisher.publish.assert_not_awaited()


@pytest.mark.asyncio
@pytest.mark.parametrize("status_code", [400, 401, 403, 404, 422])
async def test_handle_model_call_failed_all_permanent_codes_return_permanent(status_code: int):
    extract_handler = AsyncMock()
    extract_handler.handle.side_effect = ModelCallFailed(
        f"Error {status_code}", status_code=status_code
    )

    deps = _make_deps(extract_handler=extract_handler)
    handler = ProcessExtractionRequestHandler(deps)

    envelope = _make_envelope()
    outcome = await handler.handle(envelope)

    assert outcome == ProcessOutcome.PERMANENT
    deps.event_publisher.publish.assert_awaited_once()


@pytest.mark.asyncio
async def test_handle_model_call_failed_429_transient():
    extract_handler = AsyncMock()
    extract_handler.handle.side_effect = ModelCallFailed("Rate limited", status_code=429)

    deps = _make_deps(extract_handler=extract_handler)
    handler = ProcessExtractionRequestHandler(deps)

    envelope = _make_envelope()
    outcome = await handler.handle(envelope)

    assert outcome == ProcessOutcome.TRANSIENT
    deps.event_publisher.publish.assert_not_awaited()


@pytest.mark.asyncio
async def test_handle_model_call_failed_500_transient():
    extract_handler = AsyncMock()
    extract_handler.handle.side_effect = ModelCallFailed("Internal error", status_code=500)

    deps = _make_deps(extract_handler=extract_handler)
    handler = ProcessExtractionRequestHandler(deps)

    envelope = _make_envelope()
    outcome = await handler.handle(envelope)

    assert outcome == ProcessOutcome.TRANSIENT
    deps.event_publisher.publish.assert_not_awaited()


@pytest.mark.asyncio
async def test_handle_ai_model_passed_to_extract_handler():
    EXPECTED_AI_MODEL = "gpt-5.5"

    extract_handler = AsyncMock()
    extract_handler.handle.return_value = []

    deps = _make_deps(extract_handler=extract_handler)
    handler = ProcessExtractionRequestHandler(deps)

    envelope = _make_envelope(payload={"ai_model": EXPECTED_AI_MODEL})
    await handler.handle(envelope)

    extract_handler.handle.assert_awaited()
    call_kwargs = extract_handler.handle.call_args.kwargs
    assert call_kwargs["ai_model"] == EXPECTED_AI_MODEL


@pytest.mark.asyncio
async def test_handle_no_ai_model_in_payload_passes_none():
    extract_handler = AsyncMock()
    extract_handler.handle.return_value = []

    deps = _make_deps(extract_handler=extract_handler)
    handler = ProcessExtractionRequestHandler(deps)

    envelope = _make_envelope(payload={})
    await handler.handle(envelope)

    extract_handler.handle.assert_awaited()
    call_kwargs = extract_handler.handle.call_args.kwargs
    assert call_kwargs["ai_model"] is None


@pytest.mark.asyncio
async def test_handle_content_filter_400_classified_permanent_no_retry():
    extract_handler = AsyncMock()
    extract_handler.handle.side_effect = ModelCallFailed(
        "content_filter: ResponsibleAIPolicyViolation jailbreak detected", status_code=400
    )

    deps = _make_deps(extract_handler=extract_handler)
    handler = ProcessExtractionRequestHandler(deps)

    envelope = _make_envelope()
    outcome = await handler.handle(envelope)

    assert outcome == ProcessOutcome.PERMANENT
    deps.event_publisher.publish.assert_awaited_once()
    call_args = deps.event_publisher.publish.call_args
    assert call_args[0][0] == "extraction.failed"
    event = call_args[0][1]
    assert event.batch_id == _BATCH_ID
    extract_handler.handle.assert_awaited_once()
