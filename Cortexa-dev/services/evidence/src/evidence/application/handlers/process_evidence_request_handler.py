import logging
from dataclasses import dataclass
from enum import Enum

from evidence.application.dtos.store_evidence_request import StoreEvidenceRequestDto
from evidence.application.handlers.store_evidence_handler import StoreEvidenceHandler
from evidence.application.triangulation_service import TriangulationService
from evidence.domain.errors.evidence_errors import (
    CandidateNotFoundError,
    CorpusSearchError,
    EvidenceBundleBelowMinimumSourcesError,
    EvidenceBundleSourceUnavailableError,
    EvidenceCandidateDeadlineExceededError,
    LlmResearchError,
    PatentApiError,
)
from evidence.domain.events.event_envelope import EventEnvelope
from evidence.domain.events.evidence_completed import make_evidence_completed_event
from evidence.domain.events.evidence_failed import make_evidence_failed_event
from evidence.domain.models.evidence_bundle import EvidenceBundle
from evidence.domain.repositories.storage_protocols import (
    CandidateReader,
    EventPublisher,
    EvidenceBundleRepository,
)
from evidence.infrastructure.config.settings import EvidenceSettings

logger = logging.getLogger(__name__)

_TRIGGER_TYPE = "pipeline"
_PERMANENT_STATUS_CODES: frozenset[int] = frozenset({400, 401, 403, 404, 422})
_ADAPTER_ERROR_TYPES = (PatentApiError, CorpusSearchError, LlmResearchError)


class ProcessOutcome(Enum):
    SUCCESS = "success"
    PERMANENT = "permanent"
    TRANSIENT = "transient"


@dataclass
class FailureContext:
    batch_id: str
    document_id: str
    correlation_id: str | None
    candidate_id: str | None
    job_id: str
    reason: str


@dataclass
class ProcessEvidenceRequestDeps:
    repository: EvidenceBundleRepository
    publisher: EventPublisher
    triangulation: TriangulationService
    store_handler: StoreEvidenceHandler
    candidate_reader: CandidateReader
    settings: EvidenceSettings


_NON_RETRYABLE_ERROR_TYPES = (
    CandidateNotFoundError,
    EvidenceBundleBelowMinimumSourcesError,
    EvidenceBundleSourceUnavailableError,
    EvidenceCandidateDeadlineExceededError,
)


def _format_exc(exc: BaseException) -> str:
    parts = [f"{type(exc).__name__}: {exc!r}"]
    cause = exc.__cause__ or exc.__context__
    if cause is not None:
        parts.append(f"caused by {type(cause).__name__}: {cause!r}")
    return " — ".join(parts)


def _classify_error(exc: Exception) -> ProcessOutcome:
    if isinstance(exc, _NON_RETRYABLE_ERROR_TYPES):
        return ProcessOutcome.PERMANENT
    if isinstance(exc, _ADAPTER_ERROR_TYPES):
        if getattr(exc, "status_code", None) in _PERMANENT_STATUS_CODES:
            return ProcessOutcome.PERMANENT
    return ProcessOutcome.TRANSIENT


class ProcessEvidenceRequestHandler:
    def __init__(self, deps: ProcessEvidenceRequestDeps) -> None:
        self._repository = deps.repository
        self._publisher = deps.publisher
        self._triangulation = deps.triangulation
        self._store_handler = deps.store_handler
        self._candidate_reader = deps.candidate_reader
        self._settings = deps.settings

    async def handle(self, envelope: EventEnvelope) -> ProcessOutcome:
        candidate_id = envelope.payload.get("candidate_id")
        job_id = envelope.payload.get("job_id", envelope.batch_id)
        ai_model = envelope.payload.get("ai_model")
        if not candidate_id:
            logger.error(
                "batch_id=%s document_id=%s correlation_id=%s "
                "outcome=PERMANENT reason=missing_candidate_fields",
                envelope.batch_id,
                envelope.document_id,
                envelope.correlation_id,
            )
            return await self._fail(
                FailureContext(
                    batch_id=envelope.batch_id,
                    document_id=envelope.document_id,
                    correlation_id=envelope.correlation_id,
                    candidate_id=candidate_id,
                    job_id=job_id,
                    reason="missing_candidate_fields",
                )
            )
        try:
            return await self._execute(envelope, candidate_id, job_id, ai_model)
        except Exception as exc:
            return await self._classify_and_log(exc, envelope, candidate_id, job_id)

    async def _execute(
        self,
        envelope: EventEnvelope,
        candidate_id: str,
        job_id: str,
        ai_model: str | None,
    ) -> ProcessOutcome:
        existing = await self._repository.find_for_candidate(
            envelope.batch_id, envelope.document_id, candidate_id
        )
        if existing is not None:
            return await self._handle_idempotent(existing, envelope)
        candidate = await self._candidate_reader.get_by_candidate_id(
            candidate_id, envelope.batch_id
        )
        return await self._triangulate_and_store(
            envelope,
            candidate_id,
            candidate.claim_text,
            candidate.tech_field,
            job_id,
            ai_model,
        )

    async def _handle_idempotent(
        self, bundle: EvidenceBundle, envelope: EventEnvelope
    ) -> ProcessOutcome:
        event = make_evidence_completed_event(bundle, envelope.correlation_id)
        await self._publisher.publish(self._settings.evidence_completed_topic, event)
        logger.info(
            "batch_id=%s document_id=%s candidate_id=%s correlation_id=%s "
            "outcome=SUCCESS reason=idempotent",
            envelope.batch_id,
            envelope.document_id,
            bundle.candidate_id,
            envelope.correlation_id,
        )
        return ProcessOutcome.SUCCESS

    async def _triangulate_and_store(
        self,
        envelope: EventEnvelope,
        candidate_id: str,
        claim_text: str,
        tech_field: str,
        job_id: str,
        ai_model: str | None = None,
    ) -> ProcessOutcome:
        bundle = await self._triangulation.triangulate(
            candidate_id=candidate_id,
            document_id=envelope.document_id,
            claim_text=claim_text,
            tech_field=tech_field,
            batch_id=envelope.batch_id,
            job_id=job_id,
            ai_model=ai_model,
        )
        request = StoreEvidenceRequestDto(bundle=bundle, correlation_id=envelope.correlation_id)
        response = await self._store_handler.handle(request)
        if not response.published:
            logger.warning(
                "batch_id=%s document_id=%s candidate_id=%s correlation_id=%s "
                "outcome=TRANSIENT reason=publish_failed",
                envelope.batch_id,
                envelope.document_id,
                candidate_id,
                envelope.correlation_id,
            )
            return ProcessOutcome.TRANSIENT
        logger.info(
            "batch_id=%s document_id=%s candidate_id=%s correlation_id=%s outcome=SUCCESS",
            envelope.batch_id,
            envelope.document_id,
            candidate_id,
            envelope.correlation_id,
        )
        return ProcessOutcome.SUCCESS

    async def fail_on_exhaustion(self, envelope: EventEnvelope) -> None:
        candidate_id = envelope.payload.get("candidate_id")
        job_id = envelope.payload.get("job_id", envelope.batch_id)
        logger.error(
            "batch_id=%s document_id=%s candidate_id=%s correlation_id=%s "
            "outcome=PERMANENT reason=RetriesExhausted",
            envelope.batch_id,
            envelope.document_id,
            candidate_id,
            envelope.correlation_id,
        )
        await self._fail(
            FailureContext(
                batch_id=envelope.batch_id,
                document_id=envelope.document_id,
                correlation_id=envelope.correlation_id,
                candidate_id=candidate_id,
                job_id=job_id,
                reason="RetriesExhausted",
            )
        )

    async def _fail(self, context: FailureContext) -> ProcessOutcome:
        event = make_evidence_failed_event(
            batch_id=context.batch_id,
            document_id=context.document_id,
            reason=context.reason,
            job_id=context.job_id,
            candidate_id=context.candidate_id,
            trigger_type=_TRIGGER_TYPE,
            correlation_id=context.correlation_id,
        )
        await self._publisher.publish(self._settings.evidence_failed_topic, event)
        return ProcessOutcome.PERMANENT

    @staticmethod
    def _log_failure(
        exc: Exception,
        envelope: EventEnvelope,
        candidate_id: str,
        outcome: ProcessOutcome,
        error_desc: str,
    ) -> None:
        deadline_fields = ""
        if isinstance(exc, EvidenceCandidateDeadlineExceededError):
            deadline_fields = (
                f" pending_sources={exc.pending_sources} elapsed_seconds={exc.elapsed_seconds:.1f}"
            )
        logger.error(
            "batch_id=%s document_id=%s candidate_id=%s correlation_id=%s outcome=%s error=%s%s",
            envelope.batch_id,
            envelope.document_id,
            candidate_id,
            envelope.correlation_id,
            outcome.value,
            error_desc,
            deadline_fields,
            exc_info=exc,
        )

    async def _classify_and_log(
        self, exc: Exception, envelope: EventEnvelope, candidate_id: str, job_id: str
    ) -> ProcessOutcome:
        outcome = _classify_error(exc)
        error_desc = _format_exc(exc)
        self._log_failure(exc, envelope, candidate_id, outcome, error_desc)
        if outcome == ProcessOutcome.PERMANENT:
            return await self._fail(
                FailureContext(
                    batch_id=envelope.batch_id,
                    document_id=envelope.document_id,
                    correlation_id=envelope.correlation_id,
                    candidate_id=candidate_id,
                    job_id=job_id,
                    reason=error_desc,
                )
            )
        return outcome
