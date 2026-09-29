import logging
from dataclasses import dataclass
from uuid import uuid4

from extraction.application.dtos.extraction_completed_request import ExtractionCompletedRequest
from extraction.application.dtos.store_extraction_request import StoreExtractionRequest
from extraction.application.dtos.store_extraction_response import StoreExtractionResponse
from extraction.domain.errors.storage_errors import (
    PartialSaveError,
    RollbackError,
    StorageWriteError,
)
from extraction.domain.events.extraction_completed import make_extraction_completed_event
from extraction.domain.models.invention_candidate import InventionCandidate
from extraction.domain.repositories.storage_protocols import CandidateRepository, EventPublisher

logger = logging.getLogger(__name__)


@dataclass
class StoreExtractionDeps:
    candidate_repo: CandidateRepository
    event_publisher: EventPublisher
    extraction_completed_topic: str


class StoreExtractionHandler:
    def __init__(self, deps: StoreExtractionDeps) -> None:
        self._repo = deps.candidate_repo
        self._publisher = deps.event_publisher
        self._topic = deps.extraction_completed_topic

    async def handle(self, request: StoreExtractionRequest) -> StoreExtractionResponse:
        saved_ids: list[str] = []
        try:
            saved_ids = await self._repo.save_many(request.candidates)
            correlation_id = request.correlation_id or str(uuid4())
            event = make_extraction_completed_event(
                batch_id=request.job_id,
                document_id=request.document_id,
                candidate_ids=saved_ids,
                job_id=request.job_id,
                trigger_type=request.trigger_type,
                correlation_id=correlation_id,
            )
            await self._publisher.publish(self._topic, event)
        except RollbackError:
            raise
        except PartialSaveError as exc:
            logger.error(
                "Partial save failure: %s (type=%s). Rolling back %d written candidates.",
                str(exc),
                type(exc).__name__,
                len(exc.saved_ids),
            )
            await self._rollback(request.job_id, exc.saved_ids)
            raise StorageWriteError(
                f"Storage operation failed: {type(exc).__name__}: {str(exc)}"
            ) from exc
        except Exception as exc:
            logger.error(
                "Extraction storage failed: %s (type=%s). Starting rollback.",
                str(exc),
                type(exc).__name__,
            )
            await self._rollback(request.job_id, saved_ids)
            raise StorageWriteError(
                f"Storage operation failed: {type(exc).__name__}: {str(exc)}"
            ) from exc
        return StoreExtractionResponse(
            document_id=request.document_id,
            candidate_count=len(saved_ids),
            candidate_ids=saved_ids,
        )

    async def save_batch(self, candidates: list[InventionCandidate]) -> list[str]:
        if not candidates:
            return []
        try:
            return await self._repo.save_many(candidates)
        except PartialSaveError as exc:
            logger.error(
                "Partial unit save failure: %s (type=%s). Rolling back %d written candidates.",
                str(exc),
                type(exc).__name__,
                len(exc.saved_ids),
            )
            await self._rollback(candidates[0].batch_id, exc.saved_ids)
            raise StorageWriteError(
                f"Storage operation failed: {type(exc).__name__}: {str(exc)}"
            ) from exc
        except Exception as exc:
            logger.error("Extraction unit save failed: %s (type=%s).", str(exc), type(exc).__name__)
            raise StorageWriteError(
                f"Storage operation failed: {type(exc).__name__}: {str(exc)}"
            ) from exc

    async def publish_completed(
        self, request: ExtractionCompletedRequest
    ) -> StoreExtractionResponse:
        try:
            event = make_extraction_completed_event(
                batch_id=request.job_id,
                document_id=request.document_id,
                candidate_ids=request.candidate_ids,
                job_id=request.job_id,
                trigger_type=request.trigger_type,
                correlation_id=request.correlation_id,
                unit_index=request.unit_index,
                unit_count=request.unit_count,
            )
            await self._publisher.publish(self._topic, event)
        except Exception as exc:
            logger.error(
                "Extraction completed publish failed: %s (type=%s).",
                str(exc),
                type(exc).__name__,
            )
            raise StorageWriteError(
                f"Storage operation failed: {type(exc).__name__}: {str(exc)}"
            ) from exc
        return StoreExtractionResponse(
            document_id=request.document_id,
            candidate_count=len(request.candidate_ids),
            candidate_ids=request.candidate_ids,
        )

    async def _rollback(self, batch_id: str, candidate_ids: list[str]) -> None:
        if not candidate_ids:
            return
        errors: list[str] = []
        try:
            await self._repo.delete_many(batch_id, candidate_ids)
        except Exception as e:
            errors.append(f"candidates rollback: {e}")
        if errors:
            raise RollbackError(f"Rollback failures: {'; '.join(errors)}")
