import logging
from dataclasses import dataclass, field

from ingestion.application.dtos.ingest_request import IngestRequest
from ingestion.application.dtos.ingest_response import IngestResponse
from ingestion.domain.enums.source_kind import SourceKind
from ingestion.domain.errors.storage_errors import RollbackError, StorageWriteError
from ingestion.domain.events.ingestion_completed import make_ingestion_completed_event
from ingestion.domain.models.document import Document
from ingestion.domain.repositories.storage_protocols import (
    BlobRepository,
    ChunkRepository,
    DocumentRepository,
    EventPublisher,
    ProvenanceRepository,
)

logger = logging.getLogger(__name__)


@dataclass
class StoreIngestionDeps:
    blob_repo: BlobRepository
    document_repo: DocumentRepository
    chunk_repo: ChunkRepository
    provenance_repo: ProvenanceRepository
    event_publisher: EventPublisher
    ingestion_completed_topic: str
    viewable_blob_repo: BlobRepository | None = None


@dataclass
class _RollbackState:
    batch_id: str
    document_id: str
    blob_uri: str
    chunk_ids: list[str]
    blob_saved: bool = field(default=False)
    doc_saved: bool = field(default=False)
    chunks_saved: bool = field(default=False)
    provenance_saved: bool = field(default=False)
    viewable_blob_uri: str | None = None
    viewable_blob_saved: bool = field(default=False)


class StoreIngestionHandler:
    def __init__(self, deps: StoreIngestionDeps) -> None:
        self._blob = deps.blob_repo
        self._docs = deps.document_repo
        self._chunks = deps.chunk_repo
        self._provenance = deps.provenance_repo
        self._publisher = deps.event_publisher
        self._topic = deps.ingestion_completed_topic
        self._viewable_blob = deps.viewable_blob_repo

    async def handle(self, request: IngestRequest) -> IngestResponse:
        provenance_map_id = f"{request.batch_id}:{request.document_id}"
        chunk_count = len(request.chunks)
        blob_uri = self._blob.build_uri(request.batch_id, request.document_id)

        await self._store_all(request, blob_uri, provenance_map_id, chunk_count)
        await self._try_mark_completed(request.document_id, request.batch_id)

        return IngestResponse(
            document_id=request.document_id,
            blob_uri=blob_uri,
            chunk_count=chunk_count,
            provenance_map_id=provenance_map_id,
        )

    async def _store_all(
        self, request: IngestRequest, blob_uri: str, provenance_map_id: str, chunk_count: int
    ) -> None:
        chunk_ids = [e.chunk_id for e in request.provenance_map.entries]
        state = _RollbackState(
            batch_id=request.batch_id,
            document_id=request.document_id,
            blob_uri=blob_uri,
            chunk_ids=chunk_ids,
        )
        try:
            if request.source_kind == SourceKind.CODE:
                state.blob_saved = False
            else:
                await self._blob.save(request.batch_id, request.document_id, request.raw_content)
                state.blob_saved = True

            await self._maybe_save_viewable_pdf(request, state)

            await self._docs.save(
                self._build_document(
                    request, blob_uri, provenance_map_id, chunk_count, state.viewable_blob_uri
                )
            )
            state.doc_saved = True
            await self._chunks.save_many(
                request.batch_id, request.document_id, self._pair_chunks_with_ids(request)
            )
            state.chunks_saved = True
            await self._provenance.save(request.provenance_map)
            state.provenance_saved = True
            event = make_ingestion_completed_event(
                batch_id=request.batch_id,
                document_id=request.document_id,
                blob_uri=blob_uri,
                chunk_count=chunk_count,
                provenance_map_id=provenance_map_id,
                correlation_id=request.correlation_id,
            )
            await self._publisher.publish(self._topic, event)
        except Exception as exc:
            logger.error("Ingestion storage failed: %s. Starting rollback.", exc)
            await self._rollback(state)
            raise StorageWriteError(str(exc)) from exc

    async def _maybe_save_viewable_pdf(self, request: IngestRequest, state: _RollbackState) -> None:
        if (
            self._viewable_blob is None
            or request.viewable_pdf is None
            or request.source_kind == SourceKind.CODE
        ):
            return
        state.viewable_blob_uri = await self._viewable_blob.save(
            request.batch_id, request.document_id, request.viewable_pdf
        )
        state.viewable_blob_saved = True

    async def _try_mark_completed(self, document_id: str, batch_id: str) -> None:
        try:
            await self._docs.mark_completed(document_id, batch_id)
        except Exception as exc:
            logger.warning(
                "document_id=%s batch_id=%s Failed to mark document as completed: %s",
                document_id,
                batch_id,
                exc,
            )

    def _build_document(
        self,
        request: IngestRequest,
        blob_uri: str,
        provenance_map_id: str,
        chunk_count: int,
        viewable_blob_uri: str | None,
    ) -> Document:
        return Document(
            id=request.document_id,
            batch_id=request.batch_id,
            blob_uri=blob_uri,
            filename=request.filename,
            source_kind=request.source_kind,
            provenance_map_id=provenance_map_id,
            chunk_count=chunk_count,
            viewable_blob_uri=viewable_blob_uri,
        )

    def _pair_chunks_with_ids(self, request: IngestRequest) -> list[tuple[str, object]]:
        sorted_entries = sorted(request.provenance_map.entries, key=lambda e: e.order_index)
        sorted_chunks = sorted(request.chunks, key=lambda c: c.order_index)
        return [(entry.chunk_id, chunk) for entry, chunk in zip(sorted_entries, sorted_chunks)]

    async def _rollback(self, state: _RollbackState) -> None:
        errors: list[str] = []

        if state.provenance_saved:
            try:
                await self._provenance.delete_map(state.batch_id, state.chunk_ids)
            except Exception as e:
                errors.append(f"provenance rollback: {e}")

        if state.chunks_saved:
            try:
                await self._chunks.delete_many(state.batch_id, state.chunk_ids)
            except Exception as e:
                errors.append(f"chunks rollback: {e}")

        if state.doc_saved:
            try:
                await self._docs.delete(state.document_id, state.batch_id)
            except Exception as e:
                errors.append(f"document rollback: {e}")

        if state.blob_saved:
            try:
                await self._blob.delete(state.blob_uri)
            except Exception as e:
                errors.append(f"blob rollback: {e}")

        if (
            state.viewable_blob_saved
            and self._viewable_blob is not None
            and state.viewable_blob_uri
        ):
            try:
                await self._viewable_blob.delete(state.viewable_blob_uri)
            except Exception as e:
                errors.append(f"viewable blob rollback: {e}")

        if errors:
            raise RollbackError(f"Rollback failures (orphaned state): {'; '.join(errors)}")
