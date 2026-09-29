from unittest.mock import AsyncMock, MagicMock

import pytest

from ingestion.application.dtos.ingest_request import IngestRequest
from ingestion.application.handlers.store_ingestion_handler import (
    StoreIngestionDeps,
    StoreIngestionHandler,
)
from ingestion.domain.enums.source_kind import SourceKind
from ingestion.domain.errors.storage_errors import RollbackError, StorageWriteError
from ingestion.domain.models.chunk import Chunk
from ingestion.domain.models.provenance_entry import ProvenanceEntry
from ingestion.domain.models.provenance_map import ProvenanceMap

BATCH_ID = "batch-001"
DOCUMENT_ID = "doc-001"
FILENAME = "paper.pdf"
CORRELATION_ID = "corr-001"
BLOB_URI = "https://storage.example.com/blobs/batch-001/doc-001"
TOPIC = "ingestion.completed"
CHUNK_ID_1 = "chunk-aaa"
CHUNK_ID_2 = "chunk-bbb"
PROVENANCE_MAP_ID = f"{BATCH_ID}:{DOCUMENT_ID}"


def _make_chunks() -> list[Chunk]:
    return [
        Chunk(text="First chunk", order_index=0, start_char=0, end_char=11, token_count=3),
        Chunk(text="Second chunk", order_index=1, start_char=12, end_char=24, token_count=3),
    ]


def _make_provenance_map() -> ProvenanceMap:
    entries = [
        ProvenanceEntry(
            chunk_id=CHUNK_ID_1,
            source_kind=SourceKind.PAPER,
            order_index=0,
            doc_id=DOCUMENT_ID,
            byte_range=(0, 11),
        ),
        ProvenanceEntry(
            chunk_id=CHUNK_ID_2,
            source_kind=SourceKind.PAPER,
            order_index=1,
            doc_id=DOCUMENT_ID,
            byte_range=(12, 24),
        ),
    ]
    return ProvenanceMap(batch_id=BATCH_ID, entries=entries)


def _make_request(
    viewable_pdf: bytes | None = None, source_kind: SourceKind = SourceKind.PAPER
) -> IngestRequest:
    return IngestRequest(
        batch_id=BATCH_ID,
        document_id=DOCUMENT_ID,
        filename=FILENAME,
        source_kind=source_kind,
        raw_content=b"raw bytes",
        chunks=_make_chunks(),
        provenance_map=_make_provenance_map(),
        correlation_id=CORRELATION_ID,
        viewable_pdf=viewable_pdf,
    )


def _make_blob_mock(save_side_effect=None) -> MagicMock:
    return MagicMock(
        build_uri=MagicMock(return_value=BLOB_URI),
        save=AsyncMock(side_effect=save_side_effect) if save_side_effect else AsyncMock(),
        delete=AsyncMock(),
    )


def _make_doc_mock(save_side_effect=None) -> MagicMock:
    return MagicMock(
        save=AsyncMock(side_effect=save_side_effect) if save_side_effect else AsyncMock(),
        delete=AsyncMock(),
        mark_completed=AsyncMock(),
    )


VIEWABLE_BLOB_URI = "https://storage.example.com/blobs/viewable-docs/batch-001/doc-001.pdf"


def _make_viewable_blob_mock(save_side_effect=None) -> MagicMock:
    return MagicMock(
        save=AsyncMock(side_effect=save_side_effect)
        if save_side_effect
        else AsyncMock(return_value=VIEWABLE_BLOB_URI),
        delete=AsyncMock(),
    )


def _make_handler(
    blob_repo=None,
    document_repo=None,
    chunk_repo=None,
    provenance_repo=None,
    event_publisher=None,
    viewable_blob_repo=None,
) -> StoreIngestionHandler:
    blob = blob_repo or _make_blob_mock()
    docs = document_repo or _make_doc_mock()
    chunks = chunk_repo or MagicMock(save_many=AsyncMock(), delete_many=AsyncMock())
    provenance = provenance_repo or MagicMock(save=AsyncMock(), delete_map=AsyncMock())
    publisher = event_publisher or MagicMock(publish=AsyncMock())
    return StoreIngestionHandler(
        deps=StoreIngestionDeps(
            blob_repo=blob,
            document_repo=docs,
            chunk_repo=chunks,
            provenance_repo=provenance,
            event_publisher=publisher,
            ingestion_completed_topic=TOPIC,
            viewable_blob_repo=viewable_blob_repo,
        )
    )


async def test_handle_all_stores_succeed_returns_correct_response():
    handler = _make_handler()
    request = _make_request()

    response = await handler.handle(request)

    assert response.document_id == DOCUMENT_ID
    assert response.blob_uri == BLOB_URI
    assert response.chunk_count == 2
    assert response.provenance_map_id == PROVENANCE_MAP_ID


async def test_handle_all_stores_succeed_publishes_event_once():
    publisher = MagicMock(publish=AsyncMock())
    handler = _make_handler(event_publisher=publisher)
    request = _make_request()

    await handler.handle(request)

    publisher.publish.assert_awaited_once_with(TOPIC, publisher.publish.call_args[0][1])


async def test_handle_all_stores_succeed_calls_mark_completed():
    docs = _make_doc_mock()
    handler = _make_handler(document_repo=docs)

    await handler.handle(_make_request())

    docs.mark_completed.assert_awaited_once_with(DOCUMENT_ID, BATCH_ID)


async def test_handle_mark_completed_failure_does_not_raise():
    docs = _make_doc_mock()
    docs.mark_completed = AsyncMock(side_effect=RuntimeError("cosmos down"))
    handler = _make_handler(document_repo=docs)

    response = await handler.handle(_make_request())

    assert response.document_id == DOCUMENT_ID


async def test_handle_blob_uri_derived_from_build_uri():
    blob = _make_blob_mock()
    handler = _make_handler(blob_repo=blob)

    response = await handler.handle(_make_request())

    blob.build_uri.assert_called_once_with(BATCH_ID, DOCUMENT_ID)
    assert response.blob_uri == BLOB_URI


async def test_handle_blob_save_fails_raises_storage_write_error():
    blob = _make_blob_mock(save_side_effect=RuntimeError("blob down"))
    handler = _make_handler(blob_repo=blob)

    with pytest.raises(StorageWriteError):
        await handler.handle(_make_request())


async def test_handle_blob_save_fails_no_rollback_calls():
    blob = _make_blob_mock(save_side_effect=RuntimeError("blob down"))
    docs = _make_doc_mock()
    chunks = MagicMock(save_many=AsyncMock(), delete_many=AsyncMock())
    provenance = MagicMock(save=AsyncMock(), delete_map=AsyncMock())
    handler = _make_handler(
        blob_repo=blob, document_repo=docs, chunk_repo=chunks, provenance_repo=provenance
    )

    with pytest.raises(StorageWriteError):
        await handler.handle(_make_request())

    blob.delete.assert_not_awaited()
    docs.delete.assert_not_awaited()
    chunks.delete_many.assert_not_awaited()
    provenance.delete_map.assert_not_awaited()


async def test_handle_document_save_fails_raises_storage_write_error():
    docs = _make_doc_mock(save_side_effect=RuntimeError("cosmos down"))
    handler = _make_handler(document_repo=docs)

    with pytest.raises(StorageWriteError):
        await handler.handle(_make_request())


async def test_handle_document_save_fails_rolls_back_blob_only():
    blob = _make_blob_mock()
    docs = _make_doc_mock(save_side_effect=RuntimeError("cosmos down"))
    chunks = MagicMock(save_many=AsyncMock(), delete_many=AsyncMock())
    provenance = MagicMock(save=AsyncMock(), delete_map=AsyncMock())
    handler = _make_handler(
        blob_repo=blob, document_repo=docs, chunk_repo=chunks, provenance_repo=provenance
    )

    with pytest.raises(StorageWriteError):
        await handler.handle(_make_request())

    blob.delete.assert_awaited_once_with(BLOB_URI)
    docs.delete.assert_not_awaited()
    chunks.delete_many.assert_not_awaited()
    provenance.delete_map.assert_not_awaited()


async def test_handle_chunks_save_fails_raises_storage_write_error():
    chunks = MagicMock(
        save_many=AsyncMock(side_effect=RuntimeError("chunk write failed")), delete_many=AsyncMock()
    )
    handler = _make_handler(chunk_repo=chunks)

    with pytest.raises(StorageWriteError):
        await handler.handle(_make_request())


async def test_handle_chunks_save_fails_rolls_back_doc_and_blob():
    blob = _make_blob_mock()
    docs = _make_doc_mock()
    chunks = MagicMock(
        save_many=AsyncMock(side_effect=RuntimeError("chunk write failed")), delete_many=AsyncMock()
    )
    provenance = MagicMock(save=AsyncMock(), delete_map=AsyncMock())
    handler = _make_handler(
        blob_repo=blob, document_repo=docs, chunk_repo=chunks, provenance_repo=provenance
    )

    with pytest.raises(StorageWriteError):
        await handler.handle(_make_request())

    docs.delete.assert_awaited_once_with(DOCUMENT_ID, BATCH_ID)
    blob.delete.assert_awaited_once_with(BLOB_URI)
    chunks.delete_many.assert_not_awaited()
    provenance.delete_map.assert_not_awaited()


async def test_handle_provenance_save_fails_raises_storage_write_error():
    provenance = MagicMock(
        save=AsyncMock(side_effect=RuntimeError("provenance write failed")), delete_map=AsyncMock()
    )
    handler = _make_handler(provenance_repo=provenance)

    with pytest.raises(StorageWriteError):
        await handler.handle(_make_request())


async def test_handle_provenance_save_fails_rolls_back_all_three():
    blob = _make_blob_mock()
    docs = _make_doc_mock()
    chunks = MagicMock(save_many=AsyncMock(), delete_many=AsyncMock())
    provenance = MagicMock(
        save=AsyncMock(side_effect=RuntimeError("provenance write failed")),
        delete_map=AsyncMock(),
    )
    handler = _make_handler(
        blob_repo=blob, document_repo=docs, chunk_repo=chunks, provenance_repo=provenance
    )

    with pytest.raises(StorageWriteError):
        await handler.handle(_make_request())

    provenance.delete_map.assert_not_awaited()
    chunks.delete_many.assert_awaited_once_with(BATCH_ID, [CHUNK_ID_1, CHUNK_ID_2])
    docs.delete.assert_awaited_once_with(DOCUMENT_ID, BATCH_ID)
    blob.delete.assert_awaited_once_with(BLOB_URI)


async def test_handle_saves_viewable_pdf_and_sets_document_uri():
    viewable = _make_viewable_blob_mock()
    docs = _make_doc_mock()
    handler = _make_handler(document_repo=docs, viewable_blob_repo=viewable)
    request = _make_request(viewable_pdf=b"%PDF-1.4 canonical")

    await handler.handle(request)

    viewable.save.assert_awaited_once_with(BATCH_ID, DOCUMENT_ID, b"%PDF-1.4 canonical")
    saved_document = docs.save.call_args[0][0]
    assert saved_document.viewable_blob_uri == VIEWABLE_BLOB_URI


async def test_handle_no_viewable_pdf_skips_viewable_save():
    viewable = _make_viewable_blob_mock()
    handler = _make_handler(viewable_blob_repo=viewable)
    request = _make_request(viewable_pdf=None)

    await handler.handle(request)

    viewable.save.assert_not_awaited()


async def test_handle_no_viewable_blob_repo_configured_skips_viewable_save():
    handler = _make_handler(viewable_blob_repo=None)
    request = _make_request(viewable_pdf=b"%PDF-1.4 canonical")

    response = await handler.handle(request)

    assert response.document_id == DOCUMENT_ID


async def test_handle_code_source_kind_skips_viewable_save_even_if_present():
    viewable = _make_viewable_blob_mock()
    handler = _make_handler(viewable_blob_repo=viewable)
    request = _make_request(viewable_pdf=b"%PDF-1.4 canonical", source_kind=SourceKind.CODE)

    await handler.handle(request)

    viewable.save.assert_not_awaited()


async def test_handle_viewable_save_fails_raises_storage_write_error_and_rolls_back():
    viewable = _make_viewable_blob_mock(save_side_effect=RuntimeError("viewable blob down"))
    blob = _make_blob_mock()
    handler = _make_handler(blob_repo=blob, viewable_blob_repo=viewable)
    request = _make_request(viewable_pdf=b"%PDF-1.4 canonical")

    with pytest.raises(StorageWriteError):
        await handler.handle(request)

    blob.delete.assert_awaited_once_with(BLOB_URI)


async def test_handle_document_save_fails_rolls_back_viewable_blob_too():
    viewable = _make_viewable_blob_mock()
    blob = _make_blob_mock()
    docs = _make_doc_mock(save_side_effect=RuntimeError("cosmos down"))
    handler = _make_handler(blob_repo=blob, document_repo=docs, viewable_blob_repo=viewable)
    request = _make_request(viewable_pdf=b"%PDF-1.4 canonical")

    with pytest.raises(StorageWriteError):
        await handler.handle(request)

    viewable.delete.assert_awaited_once_with(VIEWABLE_BLOB_URI)
    blob.delete.assert_awaited_once_with(BLOB_URI)


async def test_handle_rollback_failure_propagates_rollback_error():
    provenance = MagicMock(
        save=AsyncMock(side_effect=RuntimeError("provenance write failed")),
        delete_map=AsyncMock(),
    )
    chunks = MagicMock(
        save_many=AsyncMock(),
        delete_many=AsyncMock(side_effect=RuntimeError("chunks rollback exploded")),
    )
    handler = _make_handler(chunk_repo=chunks, provenance_repo=provenance)

    with pytest.raises(RollbackError):
        await handler.handle(_make_request())
