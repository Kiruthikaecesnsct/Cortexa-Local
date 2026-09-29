from datetime import UTC, datetime
from unittest.mock import AsyncMock, MagicMock

from evidence.application.dtos.store_evidence_response import StoreEvidenceResponseDto
from evidence.application.handlers.process_evidence_request_handler import (
    ProcessEvidenceRequestDeps,
    ProcessEvidenceRequestHandler,
    ProcessOutcome,
)
from evidence.domain.enums.confidence_band import ConfidenceBand
from evidence.domain.enums.evidence_source import EvidenceSource
from evidence.domain.errors.evidence_errors import (
    CandidateNotFoundError,
    CorpusSearchError,
    EventPublishError,
    EvidenceBundleSourceUnavailableError,
    LlmResearchError,
    StorageWriteError,
)
from evidence.domain.events.event_envelope import EventEnvelope
from evidence.domain.models.evidence_bundle import EvidenceBundle
from evidence.domain.repositories.storage_protocols import CandidateRecord
from evidence.infrastructure.config.settings import EvidenceSettings

BATCH_ID = "batch-001"
DOCUMENT_ID = "doc-001"
CANDIDATE_ID = "cand-001"
CORRELATION_ID = "corr-001"
CANDIDATE_TEXT = "A novel sparse quantization method for neural networks."


def _make_candidate_record() -> CandidateRecord:
    return CandidateRecord(
        id=CANDIDATE_ID,
        batch_id=BATCH_ID,
        document_id=DOCUMENT_ID,
        claim_text=CANDIDATE_TEXT,
        problem="Memory and compute constraints in neural networks.",
        tech_field="Machine Learning",
    )


def _make_bundle() -> EvidenceBundle:
    return EvidenceBundle(
        id="bundle-001",
        batch_id=BATCH_ID,
        job_id=BATCH_ID,
        candidate_id=CANDIDATE_ID,
        document_id=DOCUMENT_ID,
        hits=[],
        confidence_band=ConfidenceBand.High,
        sources_used=[EvidenceSource.PatentApi],
        source_flags={
            EvidenceSource.PatentApi: True,
            EvidenceSource.SeedCorpus: False,
            EvidenceSource.LlmResearch: False,
        },
        merged_at=datetime.now(UTC),
    )


def _make_store_response(published: bool = True) -> StoreEvidenceResponseDto:
    return StoreEvidenceResponseDto(
        bundle_id="bundle-001", document_id=DOCUMENT_ID, published=published
    )


def _make_envelope(
    *,
    candidate_id: str | None = CANDIDATE_ID,
) -> EventEnvelope:
    payload: dict = {}
    if candidate_id is not None:
        payload["candidate_id"] = candidate_id
    return EventEnvelope(
        event_type="evidence.requested",
        batch_id=BATCH_ID,
        document_id=DOCUMENT_ID,
        correlation_id=CORRELATION_ID,
        payload=payload,
    )


def _make_handler(
    *,
    existing_bundle: EvidenceBundle | None = None,
    candidate_record: CandidateRecord | None = None,
    candidate_reader_side_effect=None,
    triangulate_return: EvidenceBundle | None = None,
    triangulate_side_effect=None,
    store_return: StoreEvidenceResponseDto | None = None,
    store_side_effect=None,
    publisher_side_effect=None,
) -> ProcessEvidenceRequestHandler:
    if triangulate_return is None and triangulate_side_effect is None:
        triangulate_return = _make_bundle()
    if store_return is None and store_side_effect is None:
        store_return = _make_store_response()
    if candidate_record is None and candidate_reader_side_effect is None:
        candidate_record = _make_candidate_record()

    repository = MagicMock(find_for_candidate=AsyncMock(return_value=existing_bundle))

    if candidate_reader_side_effect is not None:
        candidate_reader = MagicMock(
            get_by_candidate_id=AsyncMock(side_effect=candidate_reader_side_effect)
        )
    else:
        candidate_reader = MagicMock(get_by_candidate_id=AsyncMock(return_value=candidate_record))

    if triangulate_side_effect is not None:
        triangulation = MagicMock(triangulate=AsyncMock(side_effect=triangulate_side_effect))
    else:
        triangulation = MagicMock(triangulate=AsyncMock(return_value=triangulate_return))

    if store_side_effect is not None:
        store_handler = MagicMock(handle=AsyncMock(side_effect=store_side_effect))
    else:
        store_handler = MagicMock(handle=AsyncMock(return_value=store_return))

    if publisher_side_effect is not None:
        publisher = MagicMock(publish=AsyncMock(side_effect=publisher_side_effect))
    else:
        publisher = MagicMock(publish=AsyncMock())

    return ProcessEvidenceRequestHandler(
        ProcessEvidenceRequestDeps(
            repository=repository,
            publisher=publisher,
            triangulation=triangulation,
            store_handler=store_handler,
            candidate_reader=candidate_reader,
            settings=EvidenceSettings(
                model_router_url="http://test-model-router",
                vector_router_url="http://test-vector-router",
            ),
        )
    )


async def test_happy_path_triangulates_stores_and_returns_success():
    handler = _make_handler()

    outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.SUCCESS
    handler._triangulation.triangulate.assert_awaited_once()
    handler._store_handler.handle.assert_awaited_once()


async def test_happy_path_preserves_correlation_id():
    handler = _make_handler()

    await handler.handle(_make_envelope())

    store_request = handler._store_handler.handle.call_args[0][0]
    assert store_request.correlation_id == CORRELATION_ID


async def test_idempotent_redelivery_skips_triangulation():
    handler = _make_handler(existing_bundle=_make_bundle())

    outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.SUCCESS
    handler._triangulation.triangulate.assert_not_awaited()
    handler._store_handler.handle.assert_not_awaited()


async def test_idempotent_redelivery_republishes_event():
    handler = _make_handler(existing_bundle=_make_bundle())

    await handler.handle(_make_envelope())

    handler._publisher.publish.assert_awaited_once()


async def test_missing_candidate_id_returns_permanent():
    handler = _make_handler()

    outcome = await handler.handle(_make_envelope(candidate_id=None))

    assert outcome == ProcessOutcome.PERMANENT
    handler._triangulation.triangulate.assert_not_awaited()


async def test_candidate_not_found_returns_permanent():
    handler = _make_handler(candidate_reader_side_effect=CandidateNotFoundError("cand-001"))

    outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.PERMANENT
    handler._triangulation.triangulate.assert_not_awaited()


async def test_permanent_400_adapter_error():
    handler = _make_handler(
        triangulate_side_effect=CorpusSearchError("bad request", status_code=400)
    )

    outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.PERMANENT


async def test_permanent_401_adapter_error():
    handler = _make_handler(
        triangulate_side_effect=LlmResearchError("unauthorized", status_code=401)
    )

    outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.PERMANENT


async def test_permanent_404_adapter_error():
    handler = _make_handler(triangulate_side_effect=LlmResearchError("not found", status_code=404))

    outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.PERMANENT


async def test_transient_5xx_adapter_error():
    handler = _make_handler(
        triangulate_side_effect=CorpusSearchError("internal server error", status_code=500)
    )

    outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.TRANSIENT


async def test_source_unavailable_returns_permanent():
    handler = _make_handler(
        triangulate_side_effect=EvidenceBundleSourceUnavailableError("all sources failed")
    )

    outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.PERMANENT


async def test_transient_storage_write_error():
    handler = _make_handler(store_side_effect=StorageWriteError("cosmos write timeout"))

    outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.TRANSIENT


async def test_published_false_returns_transient():
    handler = _make_handler(store_return=_make_store_response(published=False))

    outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.TRANSIENT


async def test_idempotent_publish_failure_returns_transient():
    handler = _make_handler(
        existing_bundle=_make_bundle(),
        publisher_side_effect=EventPublishError("Service Bus unavailable"),
    )

    outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.TRANSIENT
    handler._triangulation.triangulate.assert_not_awaited()


async def test_no_candidate_text_in_logs(caplog):
    handler = _make_handler()

    with caplog.at_level("DEBUG"):
        await handler.handle(_make_envelope())

    for record in caplog.records:
        assert CANDIDATE_TEXT not in record.getMessage()


async def test_missing_candidate_fields_publishes_evidence_failed():
    handler = _make_handler()

    await handler.handle(_make_envelope(candidate_id=None))

    handler._publisher.publish.assert_awaited_once()
    topic, event = handler._publisher.publish.call_args[0]
    assert topic == "evidence-failed"
    assert event.event_type == "evidence.failed"
    assert event.batch_id == BATCH_ID
    assert event.document_id == DOCUMENT_ID
    assert event.payload["reason"] == "missing_candidate_fields"
    assert event.payload["job_id"] == BATCH_ID
    assert event.payload["candidate_id"] is None
    assert event.payload["trigger_type"] == "pipeline"


async def test_permanent_adapter_error_publishes_evidence_failed():
    handler = _make_handler(
        triangulate_side_effect=CorpusSearchError("EPO returned 400", status_code=400)
    )

    await handler.handle(_make_envelope())

    handler._publisher.publish.assert_awaited_once()
    topic, event = handler._publisher.publish.call_args[0]
    assert topic == "evidence-failed"
    assert event.event_type == "evidence.failed"
    assert event.payload["candidate_id"] == CANDIDATE_ID
    assert "EPO returned 400" in event.payload["reason"]


async def test_transient_adapter_error_does_not_publish_failure():
    handler = _make_handler(triangulate_side_effect=CorpusSearchError("timeout", status_code=500))

    await handler.handle(_make_envelope())

    handler._publisher.publish.assert_not_awaited()


async def test_success_path_does_not_publish_failure():
    handler = _make_handler()

    await handler.handle(_make_envelope())

    calls = [c[0][1].event_type for c in handler._publisher.publish.call_args_list]
    assert "evidence.failed" not in calls


async def test_happy_path_loads_candidate_from_cosmos():
    handler = _make_handler()

    await handler.handle(_make_envelope())

    handler._candidate_reader.get_by_candidate_id.assert_awaited_once_with(CANDIDATE_ID, BATCH_ID)


async def test_happy_path_passes_candidate_claim_text_to_triangulation():
    handler = _make_handler()

    await handler.handle(_make_envelope())

    call_kwargs = handler._triangulation.triangulate.call_args.kwargs
    assert call_kwargs["claim_text"] == CANDIDATE_TEXT
    assert call_kwargs["tech_field"] == "Machine Learning"


async def test_candidate_not_found_publishes_evidence_failed():
    handler = _make_handler(candidate_reader_side_effect=CandidateNotFoundError("cand-001"))

    await handler.handle(_make_envelope())

    handler._publisher.publish.assert_awaited_once()
    topic, event = handler._publisher.publish.call_args[0]
    assert topic == "evidence-failed"
    assert event.event_type == "evidence.failed"
    assert event.payload["candidate_id"] == CANDIDATE_ID
    assert "cand-001" in event.payload["reason"]


async def test_fail_on_exhaustion_publishes_evidence_failed_with_retries_exhausted():
    handler = _make_handler()

    await handler.fail_on_exhaustion(_make_envelope())

    handler._publisher.publish.assert_awaited_once()
    topic, event = handler._publisher.publish.call_args[0]
    assert topic == "evidence-failed"
    assert event.event_type == "evidence.failed"
    assert event.batch_id == BATCH_ID
    assert event.document_id == DOCUMENT_ID
    assert event.payload["candidate_id"] == CANDIDATE_ID
    assert event.payload["job_id"] == BATCH_ID
    assert event.payload["reason"] == "RetriesExhausted"
    assert event.payload["trigger_type"] == "pipeline"


async def test_fail_on_exhaustion_missing_candidate_id_still_publishes():
    handler = _make_handler()

    await handler.fail_on_exhaustion(_make_envelope(candidate_id=None))

    handler._publisher.publish.assert_awaited_once()
    topic, event = handler._publisher.publish.call_args[0]
    assert topic == "evidence-failed"
    assert event.payload["candidate_id"] is None
    assert event.payload["reason"] == "RetriesExhausted"
