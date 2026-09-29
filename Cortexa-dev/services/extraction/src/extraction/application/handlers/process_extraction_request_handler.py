import asyncio
import logging
from dataclasses import dataclass, field
from enum import Enum

from extraction.application.dtos.extraction_completed_request import ExtractionCompletedRequest
from extraction.application.handlers.extract_candidates_handler import ExtractCandidatesHandler
from extraction.application.handlers.store_extraction_handler import StoreExtractionHandler
from extraction.domain.errors.extraction_errors import (
    CandidateParseError,
    ModelCallFailed,
    PromptBuildError,
)
from extraction.domain.events.event_envelope import EventEnvelope
from extraction.domain.events.extraction_failed import make_extraction_failed_event
from extraction.domain.models.chunk_input import ChunkInput
from extraction.domain.models.invention_candidate import InventionCandidate
from extraction.domain.repositories.storage_protocols import EventPublisher
from extraction.infrastructure.cosmos.candidate_repository import CandidateRepository
from extraction.infrastructure.cosmos.chunk_reader import ChunkReader, DocumentSourceReader

logger = logging.getLogger(__name__)

_TRIGGER_TYPE = "pipeline"
_PERMANENT_STATUS_CODES = frozenset({400, 401, 403, 404, 422})


class ProcessOutcome(Enum):
    SUCCESS = "success"
    PERMANENT = "permanent"
    TRANSIENT = "transient"


@dataclass
class DocumentMeta:
    source_kind: str
    filename: str | None


@dataclass
class ExtractionUnit:
    unit_index: int = 0
    unit_count: int = 1
    chunk_range: tuple[int, int] | None = None


@dataclass
class LoadChunksRequest:
    batch_id: str
    document_id: str
    source_kind: str
    chunk_range: tuple[int, int] | None = None


@dataclass
class PublishContext:
    batch_id: str
    document_id: str
    correlation_id: str
    unit: ExtractionUnit = field(default_factory=ExtractionUnit)


@dataclass
class ProcessExtractionDeps:
    candidate_repo: CandidateRepository
    document_reader: DocumentSourceReader
    chunk_reader: ChunkReader
    extract_handler: ExtractCandidatesHandler
    store_handler: StoreExtractionHandler
    event_publisher: EventPublisher
    extraction_failed_topic: str
    concurrency: int


class ProcessExtractionRequestHandler:
    def __init__(self, deps: ProcessExtractionDeps) -> None:
        self._candidate_repo = deps.candidate_repo
        self._document_reader = deps.document_reader
        self._chunk_reader = deps.chunk_reader
        self._extract_handler = deps.extract_handler
        self._store_handler = deps.store_handler
        self._event_publisher = deps.event_publisher
        self._extraction_failed_topic = deps.extraction_failed_topic
        self._concurrency = deps.concurrency

    async def handle(self, envelope: EventEnvelope) -> ProcessOutcome:
        if envelope.document_id is None:
            logger.error(
                "batch_id=%s correlation_id=%s outcome=PERMANENT reason=missing_document_id",
                envelope.batch_id,
                envelope.correlation_id,
            )
            return await self._fail(
                envelope.batch_id, None, envelope.correlation_id, "missing_document_id"
            )
        try:
            return await self._execute(envelope)
        except (PromptBuildError, CandidateParseError) as exc:
            return await self._handle_permanent_error(envelope, f"bad_data: {exc}")
        except ModelCallFailed as exc:
            return await self._handle_model_call_failed(envelope, exc)
        except Exception as exc:
            logger.exception(
                "batch_id=%s document_id=%s correlation_id=%s outcome=TRANSIENT error=%s",
                envelope.batch_id,
                envelope.document_id,
                envelope.correlation_id,
                exc,
            )
            return ProcessOutcome.TRANSIENT

    async def _handle_permanent_error(self, envelope: EventEnvelope, reason: str) -> ProcessOutcome:
        logger.error(
            "batch_id=%s document_id=%s correlation_id=%s outcome=PERMANENT reason=%s",
            envelope.batch_id,
            envelope.document_id,
            envelope.correlation_id,
            reason,
        )
        return await self._fail(
            envelope.batch_id,
            envelope.document_id,
            envelope.correlation_id,
            reason,
        )

    async def _handle_model_call_failed(
        self, envelope: EventEnvelope, exc: ModelCallFailed
    ) -> ProcessOutcome:
        if exc.status_code in _PERMANENT_STATUS_CODES:
            return await self._handle_permanent_error(
                envelope, f"model_error_{exc.status_code}: {exc}"
            )
        logger.exception(
            "batch_id=%s document_id=%s correlation_id=%s outcome=TRANSIENT error=%s",
            envelope.batch_id,
            envelope.document_id,
            envelope.correlation_id,
            exc,
        )
        return ProcessOutcome.TRANSIENT

    async def _execute(self, envelope: EventEnvelope) -> ProcessOutcome:
        batch_id = envelope.batch_id
        document_id = envelope.document_id
        correlation_id = envelope.correlation_id
        unit = self._resolve_unit(envelope.payload)

        if await self._candidate_repo.exists_for_unit(batch_id, document_id, unit.unit_index):
            logger.info(
                "batch_id=%s document_id=%s unit_index=%s correlation_id=%s "
                "outcome=SUCCESS reason=idempotent_republish",
                batch_id,
                document_id,
                unit.unit_index,
                correlation_id,
            )
            saved_ids = await self._candidate_repo.get_candidate_ids_for_unit(
                batch_id, document_id, unit.unit_index
            )
            context = PublishContext(batch_id, document_id, correlation_id, unit)
            return await self._store_and_publish(saved_ids, context)

        document_meta = await self._resolve_document_meta(batch_id, document_id, correlation_id)
        if document_meta is None:
            return await self._fail(batch_id, document_id, correlation_id, "document_not_found")

        chunks = await self._load_chunks(
            LoadChunksRequest(batch_id, document_id, document_meta.source_kind, unit.chunk_range)
        )
        if chunks is None:
            return await self._fail(batch_id, document_id, correlation_id, "no_chunks")

        ai_model = envelope.payload.get("ai_model") if envelope.payload else None
        candidates = await self._extract_all(
            chunks, document_id, batch_id, document_meta.filename, ai_model, unit.unit_index
        )
        saved_ids = await self._store_handler.save_batch(candidates)
        context = PublishContext(batch_id, document_id, correlation_id, unit)
        return await self._store_and_publish(saved_ids, context)

    def _resolve_unit(self, payload: dict | None) -> ExtractionUnit:
        if not payload:
            return ExtractionUnit()
        chunk_start = payload.get("chunk_start")
        chunk_end = payload.get("chunk_end")
        chunk_range = (
            (chunk_start, chunk_end) if chunk_start is not None and chunk_end is not None else None
        )
        return ExtractionUnit(
            unit_index=payload.get("unit_index", 0),
            unit_count=payload.get("unit_count", 1),
            chunk_range=chunk_range,
        )

    async def _fail(
        self,
        batch_id: str,
        document_id: str | None,
        correlation_id: str,
        reason: str,
    ) -> ProcessOutcome:
        event = make_extraction_failed_event(
            batch_id=batch_id,
            document_id=document_id,
            reason=reason,
            job_id=batch_id,
            trigger_type=_TRIGGER_TYPE,
            correlation_id=correlation_id,
        )
        await self._event_publisher.publish(self._extraction_failed_topic, event)
        return ProcessOutcome.PERMANENT

    async def _resolve_document_meta(
        self, batch_id: str, document_id: str, correlation_id: str
    ) -> DocumentMeta | None:
        meta = await self._document_reader.get_source_meta(batch_id, document_id)
        if meta is None or not meta[0]:
            logger.error(
                "batch_id=%s document_id=%s correlation_id=%s "
                "outcome=PERMANENT reason=document_not_found",
                batch_id,
                document_id,
                correlation_id,
            )
            return None
        source_kind, filename = meta
        return DocumentMeta(source_kind=source_kind, filename=filename)

    async def _load_chunks(self, request: LoadChunksRequest) -> list[ChunkInput] | None:
        chunks = await self._chunk_reader.get_chunks(
            request.batch_id,
            request.document_id,
            request.source_kind,
            order_index_range=request.chunk_range,
        )
        if not chunks:
            logger.error(
                "batch_id=%s document_id=%s outcome=PERMANENT reason=no_chunks",
                request.batch_id,
                request.document_id,
            )
            return None
        return chunks

    async def _extract_all(
        self,
        chunks: list[ChunkInput],
        document_id: str,
        batch_id: str,
        document_context: str | None,
        ai_model: str | None,
        unit_index: int,
    ) -> list[InventionCandidate]:
        semaphore = asyncio.Semaphore(self._concurrency)

        async def extract_bounded(chunk: ChunkInput) -> list[InventionCandidate] | None:
            async with semaphore:
                return await self._extract_chunk_safe(
                    chunk, document_id, batch_id, document_context, ai_model
                )

        results = await asyncio.gather(
            *[extract_bounded(c) for c in chunks], return_exceptions=True
        )
        return self._collect_candidates(results, chunks, document_id, unit_index)

    def _collect_candidates(
        self,
        results: list,
        chunks: list[ChunkInput],
        document_id: str,
        unit_index: int,
    ) -> list[InventionCandidate]:
        self._raise_if_any_exception(results)
        self._raise_if_all_failed(results, chunks, document_id)
        return self._flatten_candidates(results, unit_index)

    def _raise_if_any_exception(self, results: list) -> None:
        for result in results:
            if isinstance(result, BaseException):
                raise result

    def _raise_if_all_failed(
        self, results: list, chunks: list[ChunkInput], document_id: str
    ) -> None:
        failed = sum(1 for result in results if result is None)
        if failed == len(chunks):
            raise CandidateParseError(
                f"All {failed} chunk(s) failed to parse for document_id={document_id}"
            )

    def _flatten_candidates(self, results: list, unit_index: int) -> list[InventionCandidate]:
        candidates: list[InventionCandidate] = []
        for result in results:
            if result:
                candidates.extend(
                    c.model_copy(update={"extraction_unit_index": unit_index}) for c in result
                )
        return candidates

    async def _extract_chunk_safe(
        self,
        chunk: ChunkInput,
        document_id: str,
        batch_id: str,
        document_context: str | None,
        ai_model: str | None = None,
    ) -> list[InventionCandidate] | None:
        for attempt in range(2):
            try:
                return await self._extract_handler.handle(
                    chunk,
                    document_id,
                    batch_id,
                    document_context=document_context,
                    ai_model=ai_model,
                )
            except ModelCallFailed as exc:
                if exc.error_code == "content_filter":
                    logger.info(
                        "batch_id=%s document_id=%s chunk_index=%s "
                        "reason=content_filter_rejected categories=%s — skipping chunk",
                        batch_id,
                        document_id,
                        chunk.order_index,
                        exc.categories,
                    )
                    return None
                raise
            except CandidateParseError as exc:
                if attempt == 0:
                    logger.warning(
                        "batch_id=%s document_id=%s chunk_index=%s "
                        "reason=chunk_parse_error attempt=1 — retrying: %s",
                        batch_id,
                        document_id,
                        chunk.order_index,
                        exc,
                    )
                else:
                    logger.error(
                        "batch_id=%s document_id=%s chunk_index=%s "
                        "reason=chunk_parse_error attempt=2 — skipping chunk: %s",
                        batch_id,
                        document_id,
                        chunk.order_index,
                        exc,
                    )
                    return None
        return None

    async def _store_and_publish(
        self, saved_ids: list[str], context: PublishContext
    ) -> ProcessOutcome:
        request = ExtractionCompletedRequest(
            candidate_ids=saved_ids,
            job_id=context.batch_id,
            document_id=context.document_id,
            trigger_type=_TRIGGER_TYPE,
            correlation_id=context.correlation_id,
            unit_index=context.unit.unit_index,
            unit_count=context.unit.unit_count,
        )
        await self._store_handler.publish_completed(request)
        reason_suffix = " reason=no_candidates" if not saved_ids else ""
        logger.info(
            "batch_id=%s document_id=%s unit_index=%s correlation_id=%s "
            "outcome=SUCCESS candidate_count=%d%s",
            context.batch_id,
            context.document_id,
            context.unit.unit_index,
            context.correlation_id,
            len(saved_ids),
            reason_suffix,
        )
        return ProcessOutcome.SUCCESS
