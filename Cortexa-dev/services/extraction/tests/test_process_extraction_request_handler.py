from datetime import UTC, datetime
from unittest.mock import AsyncMock, MagicMock

from extraction.application.handlers.process_extraction_request_handler import (
    ProcessExtractionDeps,
    ProcessExtractionRequestHandler,
    ProcessOutcome,
)
from extraction.domain.errors.extraction_errors import CandidateParseError, PromptBuildError
from extraction.domain.events.event_envelope import EventEnvelope
from extraction.domain.models.chunk_input import ChunkInput
from extraction.domain.models.invention_candidate import InventionCandidate
from extraction.domain.value_objects.provenance_span import ProvenanceSpan

BATCH_ID = "batch-001"
DOCUMENT_ID = "doc-001"
CORRELATION_ID = "corr-001"
CANDIDATES_PER_CHUNK = 2
CHUNK_COUNT = 2
EXPECTED_TOTAL_CANDIDATES = CHUNK_COUNT * CANDIDATES_PER_CHUNK
EXTRACTION_FAILED_TOPIC = "extraction-failed"


def _make_envelope(
    *, document_id: str | None = DOCUMENT_ID, payload: dict | None = None
) -> EventEnvelope:
    return EventEnvelope(
        event_type="extraction.requested",
        batch_id=BATCH_ID,
        document_id=document_id,
        correlation_id=CORRELATION_ID,
        payload=payload if payload is not None else {},
    )


def _make_chunk(*, order_index: int = 0, locator: str = "chars:0-100") -> ChunkInput:
    return ChunkInput(
        text="A novel method for neural tensor compression.",
        order_index=order_index,
        source_span=ProvenanceSpan(source_kind="paper", locator=locator),
        document_id=DOCUMENT_ID,
    )


def _make_candidate(candidate_id: str = "cand-001") -> InventionCandidate:
    return InventionCandidate(
        id=candidate_id,
        document_id=DOCUMENT_ID,
        batch_id=BATCH_ID,
        claim_text="A method for compressing neural network weights using sparse quantization.",
        problem="Neural networks require excessive memory during inference.",
        mechanism="Sparse quantization reduces weight precision selectively per layer.",
        tech_field="Machine Learning",
        ipc_cpc_guess="G06N 3/08",
        source_span=ProvenanceSpan(source_kind="paper", locator="chars:0-100"),
        source_chunk_index=0,
        created_at=datetime(2026, 1, 1, tzinfo=UTC),
    )


def _make_handler(
    *,
    exists_for_unit: bool = False,
    saved_candidate_ids_for_unit: list[str] | None = None,
    source_kind: str | None = "paper",
    filename: str | None = "thesis.pdf",
    source_meta_side_effect=None,
    chunks: list | None = None,
    extract_return: list | None = None,
    extract_side_effect=None,
    save_batch_side_effect=None,
    publish_completed_side_effect=None,
    concurrency: int = 4,
) -> ProcessExtractionRequestHandler:
    if chunks is None:
        chunks = [_make_chunk()]
    if extract_return is None:
        extract_return = [_make_candidate()]

    candidate_repo = MagicMock(
        exists_for_unit=AsyncMock(return_value=exists_for_unit),
        get_candidate_ids_for_unit=AsyncMock(return_value=saved_candidate_ids_for_unit or []),
    )

    if source_meta_side_effect is not None:
        document_reader = MagicMock(get_source_meta=AsyncMock(side_effect=source_meta_side_effect))
    else:
        source_meta = None if source_kind is None else (source_kind, filename)
        document_reader = MagicMock(get_source_meta=AsyncMock(return_value=source_meta))

    chunk_reader = MagicMock(get_chunks=AsyncMock(return_value=chunks))

    if extract_side_effect is not None:
        extract_handler = MagicMock(handle=AsyncMock(side_effect=extract_side_effect))
    else:
        extract_handler = MagicMock(handle=AsyncMock(return_value=extract_return))

    if save_batch_side_effect is not None:
        save_batch = AsyncMock(side_effect=save_batch_side_effect)
    else:
        save_batch = AsyncMock(side_effect=lambda candidates: [c.id for c in candidates])

    publish_completed = AsyncMock(side_effect=publish_completed_side_effect)

    store_handler = MagicMock(save_batch=save_batch, publish_completed=publish_completed)

    event_publisher = MagicMock(publish=AsyncMock())

    return ProcessExtractionRequestHandler(
        deps=ProcessExtractionDeps(
            candidate_repo=candidate_repo,
            document_reader=document_reader,
            chunk_reader=chunk_reader,
            extract_handler=extract_handler,
            store_handler=store_handler,
            event_publisher=event_publisher,
            extraction_failed_topic=EXTRACTION_FAILED_TOPIC,
            concurrency=concurrency,
        )
    )


async def test_handle_missing_document_id_returns_permanent():
    handler = _make_handler()
    envelope = _make_envelope(document_id=None)

    outcome = await handler.handle(envelope)

    assert outcome == ProcessOutcome.PERMANENT


async def test_handle_missing_document_id_publishes_extraction_failed():
    handler = _make_handler()
    envelope = _make_envelope(document_id=None)

    await handler.handle(envelope)

    handler._event_publisher.publish.assert_awaited_once()
    topic, event = handler._event_publisher.publish.call_args[0]
    assert topic == EXTRACTION_FAILED_TOPIC
    assert event.event_type == "extraction.failed"
    assert event.batch_id == BATCH_ID
    assert event.document_id is None
    assert event.payload["reason"] == "missing_document_id"
    assert event.payload["job_id"] == BATCH_ID
    assert event.payload["trigger_type"] == "pipeline"


async def test_handle_missing_document_id_does_not_call_store():
    handler = _make_handler()
    envelope = _make_envelope(document_id=None)

    await handler.handle(envelope)

    handler._store_handler.save_batch.assert_not_awaited()
    handler._store_handler.publish_completed.assert_not_awaited()


async def test_handle_existing_candidates_returns_success_idempotent():
    handler = _make_handler(
        exists_for_unit=True, saved_candidate_ids_for_unit=["cand-already-saved"]
    )

    outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.SUCCESS
    handler._extract_handler.handle.assert_not_awaited()
    handler._store_handler.save_batch.assert_not_awaited()


async def test_handle_existing_candidates_still_publishes_completed_event():
    handler = _make_handler(
        exists_for_unit=True, saved_candidate_ids_for_unit=["cand-already-saved"]
    )

    await handler.handle(_make_envelope())

    handler._store_handler.publish_completed.assert_awaited_once()
    request = handler._store_handler.publish_completed.call_args[0][0]
    assert request.candidate_ids == ["cand-already-saved"]


async def test_handle_document_not_found_returns_permanent():
    handler = _make_handler(source_kind=None)

    outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.PERMANENT


async def test_handle_document_not_found_publishes_extraction_failed():
    handler = _make_handler(source_kind=None)

    await handler.handle(_make_envelope())

    handler._event_publisher.publish.assert_awaited_once()
    topic, event = handler._event_publisher.publish.call_args[0]
    assert topic == EXTRACTION_FAILED_TOPIC
    assert event.document_id == DOCUMENT_ID
    assert event.payload["reason"] == "document_not_found"


async def test_handle_existing_document_missing_source_kind_returns_permanent():
    handler = _make_handler(
        source_meta_side_effect=lambda batch_id, document_id: (None, "thesis.pdf")
    )

    outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.PERMANENT


async def test_handle_existing_document_missing_source_kind_publishes_document_not_found():
    handler = _make_handler(
        source_meta_side_effect=lambda batch_id, document_id: (None, "thesis.pdf")
    )

    await handler.handle(_make_envelope())

    handler._event_publisher.publish.assert_awaited_once()
    _, event = handler._event_publisher.publish.call_args[0]
    assert event.document_id == DOCUMENT_ID
    assert event.payload["reason"] == "document_not_found"


async def test_handle_existing_document_missing_source_kind_does_not_call_extract():
    handler = _make_handler(
        source_meta_side_effect=lambda batch_id, document_id: (None, "thesis.pdf")
    )

    await handler.handle(_make_envelope())

    handler._extract_handler.handle.assert_not_awaited()
    handler._store_handler.save_batch.assert_not_awaited()
    handler._store_handler.publish_completed.assert_not_awaited()


async def test_handle_no_chunks_returns_permanent():
    handler = _make_handler(chunks=[])

    outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.PERMANENT


async def test_handle_no_chunks_publishes_extraction_failed():
    handler = _make_handler(chunks=[])

    await handler.handle(_make_envelope())

    handler._event_publisher.publish.assert_awaited_once()
    topic, event = handler._event_publisher.publish.call_args[0]
    assert topic == EXTRACTION_FAILED_TOPIC
    assert event.document_id == DOCUMENT_ID
    assert event.payload["reason"] == "no_chunks"


async def test_handle_happy_path_aggregates_candidates_and_calls_store_once():
    chunk_a = _make_chunk(order_index=0, locator="chars:0-100")
    chunk_b = _make_chunk(order_index=1, locator="chars:100-200")
    per_chunk_candidates = [_make_candidate(), _make_candidate()]
    handler = _make_handler(
        chunks=[chunk_a, chunk_b],
        extract_return=per_chunk_candidates,
    )

    outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.SUCCESS
    handler._store_handler.publish_completed.assert_awaited_once()
    request = handler._store_handler.publish_completed.call_args[0][0]
    assert len(request.candidate_ids) == EXPECTED_TOTAL_CANDIDATES


async def test_handle_happy_path_preserves_correlation_id():
    handler = _make_handler()

    await handler.handle(_make_envelope())

    request = handler._store_handler.publish_completed.call_args[0][0]
    assert request.correlation_id == CORRELATION_ID


async def test_handle_prompt_build_error_returns_permanent():
    handler = _make_handler(extract_side_effect=PromptBuildError("missing template variable"))

    outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.PERMANENT


async def test_handle_prompt_build_error_publishes_extraction_failed():
    handler = _make_handler(extract_side_effect=PromptBuildError("missing template variable"))

    await handler.handle(_make_envelope())

    handler._event_publisher.publish.assert_awaited_once()
    _, event = handler._event_publisher.publish.call_args[0]
    assert event.payload["reason"].startswith("bad_data:")
    assert "missing template variable" in event.payload["reason"]


async def test_handle_candidate_parse_error_returns_permanent():
    handler = _make_handler(extract_side_effect=CandidateParseError("malformed LLM response JSON"))

    outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.PERMANENT


async def test_handle_candidate_parse_error_publishes_extraction_failed():
    handler = _make_handler(extract_side_effect=CandidateParseError("malformed LLM response JSON"))

    await handler.handle(_make_envelope())

    handler._event_publisher.publish.assert_awaited_once()
    _, event = handler._event_publisher.publish.call_args[0]
    assert event.payload["reason"].startswith("bad_data:")
    assert "All 1 chunk(s) failed to parse" in event.payload["reason"]


async def test_handle_generic_exception_returns_transient():
    handler = _make_handler(publish_completed_side_effect=RuntimeError("cosmos write timeout"))

    outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.TRANSIENT


async def test_handle_cosmos_failure_returns_transient():
    handler = _make_handler(source_meta_side_effect=RuntimeError("cosmos connection refused"))

    outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.TRANSIENT


async def test_handle_happy_path_threads_filename_as_document_context():
    handler = _make_handler(filename="Neural Compression Thesis V1.0.pdf")

    await handler.handle(_make_envelope())

    handler._extract_handler.handle.assert_awaited_once()
    _, kwargs = handler._extract_handler.handle.call_args
    assert kwargs["document_context"] == "Neural Compression Thesis V1.0.pdf"


async def test_handle_happy_path_passes_none_document_context_when_filename_missing():
    handler = _make_handler(filename=None)

    await handler.handle(_make_envelope())

    handler._extract_handler.handle.assert_awaited_once()
    _, kwargs = handler._extract_handler.handle.call_args
    assert kwargs["document_context"] is None


async def test_handle_threads_ai_model_from_envelope_payload():
    handler = _make_handler()

    await handler.handle(_make_envelope(payload={"ai_model": "gpt-5.4"}))

    handler._extract_handler.handle.assert_awaited_once()
    _, kwargs = handler._extract_handler.handle.call_args
    assert kwargs["ai_model"] == "gpt-5.4"


async def test_handle_passes_none_ai_model_when_payload_missing_key():
    handler = _make_handler()

    await handler.handle(_make_envelope())

    handler._extract_handler.handle.assert_awaited_once()
    _, kwargs = handler._extract_handler.handle.call_args
    assert kwargs["ai_model"] is None


async def test_handle_one_empty_chunk_one_real_chunk_returns_success_with_real_only():
    chunk_a = _make_chunk(order_index=0, locator="chars:0-100")
    chunk_b = _make_chunk(order_index=1, locator="chars:100-200")
    real_candidate = _make_candidate()

    def extract_side_effect(chunk, document_id, batch_id, document_context=None, ai_model=None):
        return [] if chunk.order_index == 0 else [real_candidate]

    handler = _make_handler(chunks=[chunk_a, chunk_b], extract_side_effect=extract_side_effect)

    outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.SUCCESS
    handler._store_handler.publish_completed.assert_awaited_once()
    request = handler._store_handler.publish_completed.call_args[0][0]
    assert request.candidate_ids == [real_candidate.id]


async def test_handle_all_chunks_legitimately_empty_returns_success_with_empty_store():
    chunk_a = _make_chunk(order_index=0, locator="chars:0-100")
    chunk_b = _make_chunk(order_index=1, locator="chars:100-200")
    handler = _make_handler(chunks=[chunk_a, chunk_b], extract_return=[])

    outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.SUCCESS
    handler._store_handler.publish_completed.assert_awaited_once()
    request = handler._store_handler.publish_completed.call_args[0][0]
    assert request.candidate_ids == []


async def test_handle_one_chunk_parse_error_one_success_skips_bad_chunk():
    chunk_a = _make_chunk(order_index=0, locator="chars:0-100")
    chunk_b = _make_chunk(order_index=1, locator="chars:100-200")
    good_candidate = _make_candidate()

    def extract_side_effect(chunk, document_id, batch_id, document_context=None, ai_model=None):
        if chunk.order_index == 0:
            raise CandidateParseError("malformed LLM response JSON")
        return [good_candidate]

    handler = _make_handler(chunks=[chunk_a, chunk_b], extract_side_effect=extract_side_effect)

    outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.SUCCESS
    handler._store_handler.publish_completed.assert_awaited_once()
    request = handler._store_handler.publish_completed.call_args[0][0]
    assert request.candidate_ids == [good_candidate.id]


async def test_handle_all_chunks_parse_error_returns_permanent():
    chunk_a = _make_chunk(order_index=0, locator="chars:0-100")
    chunk_b = _make_chunk(order_index=1, locator="chars:100-200")
    handler = _make_handler(
        chunks=[chunk_a, chunk_b],
        extract_side_effect=CandidateParseError("malformed LLM response JSON"),
    )

    outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.PERMANENT
    handler._store_handler.publish_completed.assert_not_awaited()


async def test_handle_all_chunks_parse_error_publishes_extraction_failed():
    chunk_a = _make_chunk(order_index=0, locator="chars:0-100")
    chunk_b = _make_chunk(order_index=1, locator="chars:100-200")
    handler = _make_handler(
        chunks=[chunk_a, chunk_b],
        extract_side_effect=CandidateParseError("malformed LLM response JSON"),
    )

    await handler.handle(_make_envelope())

    handler._event_publisher.publish.assert_awaited_once()
    topic, event = handler._event_publisher.publish.call_args[0]
    assert topic == EXTRACTION_FAILED_TOPIC
    assert event.event_type == "extraction.failed"
    assert event.payload["reason"].startswith("bad_data:")


async def test_extract_all_bounded_concurrency_preserves_all_candidates():
    import asyncio

    chunks = [_make_chunk(order_index=i) for i in range(8)]
    per_chunk_candidates = [_make_candidate(), _make_candidate()]
    call_order = []

    async def extract_side_effect(
        chunk, document_id, batch_id, document_context=None, ai_model=None
    ):
        call_order.append(chunk.order_index)
        await asyncio.sleep(0.01)
        return per_chunk_candidates

    handler = _make_handler(
        chunks=chunks,
        extract_side_effect=extract_side_effect,
        concurrency=4,
    )

    outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.SUCCESS
    handler._store_handler.publish_completed.assert_awaited_once()
    request = handler._store_handler.publish_completed.call_args[0][0]
    assert len(request.candidate_ids) == len(chunks) * len(per_chunk_candidates)
    assert len(call_order) == len(chunks)


async def test_extract_chunk_safe_retries_parse_error_then_succeeds():
    chunk = _make_chunk(order_index=0)
    candidate = _make_candidate()
    handler = _make_handler(
        chunks=[chunk],
        extract_side_effect=[CandidateParseError("bad json"), [candidate]],
    )

    outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.SUCCESS
    request = handler._store_handler.publish_completed.call_args[0][0]
    assert request.candidate_ids == [candidate.id]


async def test_handle_transient_exception_from_chunk_propagates_and_returns_transient():
    import asyncio

    chunk_a = _make_chunk(order_index=0, locator="chars:0-100")
    chunk_b = _make_chunk(order_index=1, locator="chars:100-200")
    good_candidate = _make_candidate()
    completed_calls = []

    class ModelCallFailed(RuntimeError):
        pass

    async def extract_side_effect(
        chunk, document_id, batch_id, document_context=None, ai_model=None
    ):
        await asyncio.sleep(0.01)
        completed_calls.append(chunk.order_index)
        if chunk.order_index == 0:
            raise ModelCallFailed("model timeout after retries")
        return [good_candidate]

    handler = _make_handler(
        chunks=[chunk_a, chunk_b],
        extract_side_effect=extract_side_effect,
        concurrency=2,
    )

    outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.TRANSIENT
    handler._store_handler.publish_completed.assert_not_awaited()
    assert len(completed_calls) == 2


async def test_handle_no_range_in_payload_reads_full_document():
    handler = _make_handler()

    await handler.handle(_make_envelope(payload={}))

    handler._chunk_reader.get_chunks.assert_awaited_once()
    _, kwargs = handler._chunk_reader.get_chunks.call_args
    assert kwargs["order_index_range"] is None


async def test_handle_chunk_range_in_payload_passed_to_chunk_reader():
    handler = _make_handler()

    await handler.handle(_make_envelope(payload={"chunk_start": 25, "chunk_end": 50}))

    handler._chunk_reader.get_chunks.assert_awaited_once()
    _, kwargs = handler._chunk_reader.get_chunks.call_args
    assert kwargs["order_index_range"] == (25, 50)


async def test_handle_unit_index_and_count_used_for_idempotency_check():
    handler = _make_handler()

    await handler.handle(_make_envelope(payload={"unit_index": 3, "unit_count": 20}))

    handler._candidate_repo.exists_for_unit.assert_awaited_once_with(BATCH_ID, DOCUMENT_ID, 3)


async def test_handle_default_unit_index_is_zero_when_payload_missing_unit_fields():
    handler = _make_handler()

    await handler.handle(_make_envelope(payload={}))

    handler._candidate_repo.exists_for_unit.assert_awaited_once_with(BATCH_ID, DOCUMENT_ID, 0)


async def test_handle_unit_index_and_count_published_on_completion():
    handler = _make_handler()

    await handler.handle(_make_envelope(payload={"unit_index": 5, "unit_count": 20}))

    request = handler._store_handler.publish_completed.call_args[0][0]
    assert request.unit_index == 5
    assert request.unit_count == 20


async def test_handle_second_unit_still_processes_when_first_unit_already_saved():
    handler = _make_handler(exists_for_unit=False)

    outcome = await handler.handle(_make_envelope(payload={"unit_index": 1, "unit_count": 2}))

    assert outcome == ProcessOutcome.SUCCESS
    handler._extract_handler.handle.assert_awaited_once()
    handler._store_handler.publish_completed.assert_awaited_once()


async def test_handle_redelivered_unit_already_saved_does_not_reextract_or_resave():
    handler = _make_handler(exists_for_unit=True, saved_candidate_ids_for_unit=["cand-1", "cand-2"])

    outcome = await handler.handle(_make_envelope(payload={"unit_index": 1, "unit_count": 2}))

    assert outcome == ProcessOutcome.SUCCESS
    handler._chunk_reader.get_chunks.assert_not_awaited()
    handler._extract_handler.handle.assert_not_awaited()
    handler._store_handler.save_batch.assert_not_awaited()


async def test_handle_redelivered_unit_already_saved_republishes_completion_for_saga():
    """A redelivery arriving after a unit's candidates were saved but before
    extraction.completed published (e.g. crash/lock-loss between save and publish)
    must still result in the completion event firing, or the saga hangs forever."""
    handler = _make_handler(exists_for_unit=True, saved_candidate_ids_for_unit=["cand-1", "cand-2"])

    await handler.handle(_make_envelope(payload={"unit_index": 1, "unit_count": 2}))

    handler._store_handler.publish_completed.assert_awaited_once()
    request = handler._store_handler.publish_completed.call_args[0][0]
    assert request.candidate_ids == ["cand-1", "cand-2"]
    assert request.unit_index == 1
    assert request.unit_count == 2


async def test_handle_saves_unit_atomically_in_one_batch_tagging_unit_index():
    chunk_a = _make_chunk(order_index=0, locator="chars:0-100")
    chunk_b = _make_chunk(order_index=1, locator="chars:100-200")
    candidate_a = _make_candidate("cand-a")
    candidate_b = _make_candidate("cand-b")

    def extract_side_effect(chunk, document_id, batch_id, document_context=None, ai_model=None):
        return [candidate_a] if chunk.order_index == 0 else [candidate_b]

    handler = _make_handler(
        chunks=[chunk_a, chunk_b],
        extract_side_effect=extract_side_effect,
    )

    await handler.handle(_make_envelope(payload={"unit_index": 4, "unit_count": 20}))

    # A unit is saved once, after every chunk in its range succeeds, so a
    # mid-unit crash leaves nothing committed and redelivery reprocesses cleanly.
    handler._store_handler.save_batch.assert_awaited_once()
    saved = handler._store_handler.save_batch.call_args[0][0]
    assert {c.extraction_unit_index for c in saved} == {4}
    assert len(saved) == 2


async def test_handle_unit_save_persists_before_publish_completed():
    call_sequence: list[str] = []

    async def save_batch_side_effect(candidates):
        call_sequence.append("save_batch")
        return [c.id for c in candidates]

    async def publish_completed_side_effect(request):
        call_sequence.append("publish_completed")
        return None

    handler = _make_handler(
        save_batch_side_effect=save_batch_side_effect,
        publish_completed_side_effect=publish_completed_side_effect,
    )

    await handler.handle(_make_envelope())

    assert call_sequence == ["save_batch", "publish_completed"]


async def test_handle_transient_chunk_failure_persists_no_candidates_for_unit():
    # Core BUG175 fix: extraction is atomic per bounded unit. If any chunk
    # raises a transient error the whole unit must NOT save partially — the
    # message redelivers and reprocesses the entire (small) unit from scratch,
    # so no chunk's candidates can be silently dropped.
    chunk_a = _make_chunk(order_index=0, locator="chars:0-100")
    chunk_b = _make_chunk(order_index=1, locator="chars:100-200")

    def extract_side_effect(chunk, document_id, batch_id, document_context=None, ai_model=None):
        if chunk.order_index == 0:
            return [_make_candidate("cand-a")]
        raise RuntimeError("model-router transient outage")

    handler = _make_handler(chunks=[chunk_a, chunk_b], extract_side_effect=extract_side_effect)

    outcome = await handler.handle(_make_envelope(payload={"unit_index": 4, "unit_count": 20}))

    assert outcome == ProcessOutcome.TRANSIENT
    handler._store_handler.save_batch.assert_not_awaited()
    handler._store_handler.publish_completed.assert_not_awaited()


async def test_handle_publish_failure_after_unit_saved_returns_transient():
    handler = _make_handler(publish_completed_side_effect=RuntimeError("service bus unavailable"))

    outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.TRANSIENT
    handler._store_handler.save_batch.assert_awaited_once()


async def test_handle_one_chunk_content_filter_skips_chunk_continues_with_others():
    from extraction.domain.errors.extraction_errors import ModelCallFailed

    chunk_a = _make_chunk(order_index=0, locator="chars:0-100")
    chunk_b = _make_chunk(order_index=1, locator="chars:100-200")
    chunk_c = _make_chunk(order_index=2, locator="chars:200-300")

    def extract_side_effect(chunk, document_id, batch_id, document_context=None, ai_model=None):
        if chunk.order_index == 1:
            raise ModelCallFailed(
                "model-router returned 400",
                status_code=400,
                error_code="content_filter",
                categories=["jailbreak"],
            )
        return [_make_candidate(f"cand-{chunk.order_index}")]

    handler = _make_handler(
        chunks=[chunk_a, chunk_b, chunk_c], extract_side_effect=extract_side_effect
    )

    outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.SUCCESS
    handler._store_handler.save_batch.assert_awaited_once()
    saved = handler._store_handler.save_batch.call_args[0][0]
    assert len(saved) == 2
    assert {c.id for c in saved} == {"cand-0", "cand-2"}


async def test_handle_all_chunks_content_filter_returns_permanent_no_candidates():
    from extraction.domain.errors.extraction_errors import ModelCallFailed

    chunk_a = _make_chunk(order_index=0)
    chunk_b = _make_chunk(order_index=1)

    def extract_side_effect(chunk, document_id, batch_id, document_context=None, ai_model=None):
        raise ModelCallFailed(
            "model-router returned 400",
            status_code=400,
            error_code="content_filter",
            categories=["violence"],
        )

    handler = _make_handler(chunks=[chunk_a, chunk_b], extract_side_effect=extract_side_effect)

    outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.PERMANENT
    handler._store_handler.save_batch.assert_not_awaited()
    handler._event_publisher.publish.assert_awaited_once()
    _, event = handler._event_publisher.publish.call_args[0]
    assert "All 2 chunk(s) failed to parse" in event.payload["reason"]


async def test_handle_non_content_filter_400_returns_permanent():
    from extraction.domain.errors.extraction_errors import ModelCallFailed

    chunk_a = _make_chunk(order_index=0)

    def extract_side_effect(chunk, document_id, batch_id, document_context=None, ai_model=None):
        raise ModelCallFailed(
            "model-router returned 400", status_code=400, error_code="invalid_request"
        )

    handler = _make_handler(chunks=[chunk_a], extract_side_effect=extract_side_effect)

    outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.PERMANENT
    handler._event_publisher.publish.assert_awaited_once()
    _, event = handler._event_publisher.publish.call_args[0]
    assert "model_error_400" in event.payload["reason"]
