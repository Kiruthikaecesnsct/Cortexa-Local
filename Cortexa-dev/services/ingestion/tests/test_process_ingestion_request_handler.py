from unittest.mock import AsyncMock, MagicMock, patch

from pydantic import ValidationError

from ingestion.application.handlers.process_ingestion_request_handler import (
    ProcessIngestionDeps,
    ProcessIngestionRequestHandler,
    ProcessOutcome,
)
from ingestion.application.raw_file_ingestor import IngestorConfig
from ingestion.domain.errors.parser_errors import UnsupportedFormatError
from ingestion.domain.errors.storage_errors import StorageWriteError
from ingestion.domain.events.event_envelope import EventEnvelope
from ingestion.domain.models.document_ref import DocumentRef

BATCH_ID = "batch-001"
DOCUMENT_ID = "doc-001"
CORRELATION_ID = "corr-001"
FILENAME = "paper.pdf"

_CONFIG = IngestorConfig(chunk_size=512, chunk_overlap=50, chunk_encoding="cl100k_base")


def _make_envelope(*, document_id: str | None = DOCUMENT_ID) -> EventEnvelope:
    return EventEnvelope(
        event_type="ingestion.requested",
        batch_id=BATCH_ID,
        document_id=document_id,
        correlation_id=CORRELATION_ID,
        payload={},
    )


def _make_document(*, status: str = "queued") -> DocumentRef:
    return DocumentRef(
        id=DOCUMENT_ID,
        batch_id=BATCH_ID,
        filename=FILENAME,
        status=status,
    )


def _make_handler(
    *,
    document: DocumentRef | None = None,
    blob_bytes: bytes = b"pdf-bytes",
    blob_content_type: str = "application/pdf",
    store_side_effect=None,
) -> ProcessIngestionRequestHandler:
    blob_repo = MagicMock(read=AsyncMock(return_value=(blob_bytes, blob_content_type)))
    document_repo = MagicMock(get=AsyncMock(return_value=document))
    store_handler = MagicMock(
        handle=AsyncMock(side_effect=store_side_effect) if store_side_effect else AsyncMock()
    )
    return ProcessIngestionRequestHandler(
        deps=ProcessIngestionDeps(
            blob_repo=blob_repo,
            document_repo=document_repo,
            store_handler=store_handler,
            ingestor_config=_CONFIG,
        )
    )


async def test_handle_missing_document_id_returns_permanent():
    handler = _make_handler()
    envelope = _make_envelope(document_id=None)

    outcome = await handler.handle(envelope)

    assert outcome == ProcessOutcome.PERMANENT


async def test_handle_missing_document_id_does_not_call_store():
    store = MagicMock(handle=AsyncMock())
    handler = _make_handler()
    handler._store = store
    envelope = _make_envelope(document_id=None)

    await handler.handle(envelope)

    store.handle.assert_not_awaited()


async def test_handle_idempotency_short_circuit_returns_success():
    handler = _make_handler(document=_make_document(status="completed"))

    outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.SUCCESS


async def test_handle_idempotency_short_circuit_does_not_call_store():
    store = MagicMock(handle=AsyncMock())
    handler = _make_handler(document=_make_document(status="completed"))
    handler._store = store

    await handler.handle(_make_envelope())

    store.handle.assert_not_awaited()


async def test_handle_document_not_found_returns_permanent():
    handler = _make_handler(document=None)

    outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.PERMANENT


async def test_handle_success_path_returns_success():
    with patch(
        "ingestion.application.handlers.process_ingestion_request_handler.assemble_ingest_request",
        new_callable=AsyncMock,
        return_value=MagicMock(),
    ):
        handler = _make_handler(document=_make_document())
        outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.SUCCESS


async def test_handle_unsupported_format_returns_permanent():
    with patch(
        "ingestion.application.handlers.process_ingestion_request_handler.assemble_ingest_request",
        new_callable=AsyncMock,
        side_effect=UnsupportedFormatError("bad format"),
    ):
        handler = _make_handler(document=_make_document())
        outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.PERMANENT


async def test_handle_value_error_returns_permanent():
    with patch(
        "ingestion.application.handlers.process_ingestion_request_handler.assemble_ingest_request",
        new_callable=AsyncMock,
        side_effect=ValueError("disallowed special token '<|endoftext|>'"),
    ):
        handler = _make_handler(document=_make_document())
        outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.PERMANENT


async def test_handle_storage_write_error_returns_transient():
    with patch(
        "ingestion.application.handlers.process_ingestion_request_handler.assemble_ingest_request",
        new_callable=AsyncMock,
        return_value=MagicMock(),
    ):
        handler = _make_handler(
            document=_make_document(),
            store_side_effect=StorageWriteError("cosmos down"),
        )
        outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.TRANSIENT


def _storage_error_from_value_error(message: str) -> StorageWriteError:
    # Mirror store_ingestion_handler's `raise StorageWriteError(...) from exc`,
    # which sets __cause__ to the original ValueError.
    err = StorageWriteError(message)
    err.__cause__ = ValueError(message)
    return err


async def test_handle_deterministic_storage_error_returns_permanent():
    # StorageWriteError wrapping a ValueError (e.g. Cosmos "Id contains illegal
    # chars") is deterministic — must be PERMANENT, not a transient retry loop.
    with patch(
        "ingestion.application.handlers.process_ingestion_request_handler.assemble_ingest_request",
        new_callable=AsyncMock,
        return_value=MagicMock(),
    ):
        handler = _make_handler(
            document=_make_document(),
            store_side_effect=_storage_error_from_value_error("Id contains illegal chars."),
        )
        outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.PERMANENT


async def test_handle_unexpected_exception_returns_transient():
    with patch(
        "ingestion.application.handlers.process_ingestion_request_handler.assemble_ingest_request",
        new_callable=AsyncMock,
        side_effect=RuntimeError("network blip"),
    ):
        handler = _make_handler(document=_make_document())
        outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.TRANSIENT


async def test_handle_blob_read_error_returns_transient():
    blob_repo = MagicMock(read=AsyncMock(side_effect=RuntimeError("blob unreachable")))
    document_repo = MagicMock(get=AsyncMock(return_value=_make_document()))
    store = MagicMock(handle=AsyncMock())
    handler = ProcessIngestionRequestHandler(
        deps=ProcessIngestionDeps(
            blob_repo=blob_repo,
            document_repo=document_repo,
            store_handler=store,
            ingestor_config=_CONFIG,
        )
    )

    outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.TRANSIENT


async def test_handle_queued_status_proceeds_to_pipeline():
    with patch(
        "ingestion.application.handlers.process_ingestion_request_handler.assemble_ingest_request",
        new_callable=AsyncMock,
        return_value=MagicMock(),
    ):
        handler = _make_handler(document=_make_document(status="queued"))
        store = MagicMock(handle=AsyncMock())
        handler._store = store
        outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.SUCCESS
    store.handle.assert_awaited_once()


async def test_handle_malformed_document_returns_permanent():
    document_repo = MagicMock(
        get=AsyncMock(side_effect=ValidationError.from_exception_data("Doc", []))
    )
    handler = ProcessIngestionRequestHandler(
        deps=ProcessIngestionDeps(
            blob_repo=MagicMock(read=AsyncMock()),
            document_repo=document_repo,
            store_handler=MagicMock(handle=AsyncMock()),
            ingestor_config=_CONFIG,
        )
    )

    outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.PERMANENT
