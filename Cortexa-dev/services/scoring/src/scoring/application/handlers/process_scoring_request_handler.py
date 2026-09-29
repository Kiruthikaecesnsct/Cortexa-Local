import logging
from dataclasses import dataclass
from enum import Enum

from pydantic import ValidationError

from scoring.application.claim_draft_generator import (
    ClaimDraftGenerator,
    ClaimDraftRequest,
)
from scoring.application.dtos.score_verdict_request import ScoreVerdictRequestDto
from scoring.application.handlers.score_verdict_handler import ScoreVerdictHandler
from scoring.domain.errors.scoring_errors import (
    AxisParseError,
    DualScoringFailedError,
    SingleScoringFailedError,
    UngroundedVerdictError,
)
from scoring.domain.errors.storage_errors import (
    EventPublishError,
    StorageWriteError,
    VerdictNotFoundError,
)
from scoring.domain.events.event_envelope import EventEnvelope
from scoring.domain.events.scoring_completed import make_scoring_completed_event
from scoring.domain.events.scoring_failed import make_scoring_failed_event
from scoring.domain.models.stored_verdict import StoredVerdict
from scoring.domain.repositories.storage_protocols import (
    CandidateReader,
    EventPublisher,
    EvidenceBundleReader,
    VerdictRepository,
)
from scoring.infrastructure.config.settings import ScoringSettings

logger = logging.getLogger(__name__)

_TRIGGER_TYPE = "pipeline"
_PERMANENT_ERROR_TYPES = (
    AxisParseError,
    UngroundedVerdictError,
    VerdictNotFoundError,
    ValidationError,
)
_TRANSIENT_ERROR_TYPES = (
    EventPublishError,
    StorageWriteError,
)
# Programming errors are never transient: retrying them only thrashes the Service
# Bus session lock until the message dead-letters. Fail fast as PERMANENT instead.
_PROGRAMMING_ERROR_TYPES = (
    TypeError,
    AttributeError,
    KeyError,
    NotImplementedError,
    ImportError,
)


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
class ProcessScoringDeps:
    verdict_repo: VerdictRepository
    candidate_reader: CandidateReader
    bundle_reader: EvidenceBundleReader
    score_handler: ScoreVerdictHandler
    publisher: EventPublisher
    settings: ScoringSettings
    claim_draft_generator: ClaimDraftGenerator | None = None


def _classify_error(exc: Exception) -> ProcessOutcome:
    if isinstance(exc, _PROGRAMMING_ERROR_TYPES):
        return ProcessOutcome.PERMANENT
    if isinstance(exc, _PERMANENT_ERROR_TYPES):
        return ProcessOutcome.PERMANENT
    if isinstance(exc, (SingleScoringFailedError, DualScoringFailedError)):
        if hasattr(exc, "status_code") and exc.status_code is not None:
            if 400 <= exc.status_code < 500:
                return ProcessOutcome.PERMANENT
        return ProcessOutcome.TRANSIENT
    if isinstance(exc, _TRANSIENT_ERROR_TYPES):
        return ProcessOutcome.TRANSIENT
    return ProcessOutcome.TRANSIENT


def _build_evidence_digest(bundle) -> str:
    if not bundle or not bundle.hits:
        return ""
    digest_parts = []
    for hit in bundle.hits[:3]:
        title = (hit.title or "")[:80].strip()
        citation = (hit.citation or "")[:60].strip()
        abstract = (hit.abstract or "")[:200].strip()
        if title or citation:
            digest_parts.append(f"Title: {title}\nCitation: {citation}\nAbstract: {abstract}")
    return "\n\n".join(digest_parts)


def _build_draft_request(candidate, bundle, envelope: EventEnvelope) -> ClaimDraftRequest:
    evidence_digest = _build_evidence_digest(bundle)
    return ClaimDraftRequest(
        claim_text=candidate.claim_text,
        problem=candidate.problem or "",
        mechanism=candidate.mechanism or "",
        tech_field=candidate.tech_field or "",
        evidence_digest=evidence_digest,
        correlation_id=envelope.correlation_id,
        model=envelope.payload.get("ai_model"),
    )


async def _update_verdict_with_draft(
    verdict_repo: VerdictRepository,
    candidate_id: str,
    batch_id: str,
    result,
    correlation_id: str | None,
) -> None:
    verdict = await verdict_repo.find_by_candidate(candidate_id, batch_id)
    if verdict is not None:
        verdict.drafted_claim = result.drafted_claim
        verdict.claim_draft_status = result.status.value
        await verdict_repo.save(verdict)
        logger.info(
            "batch_id=%s candidate_id=%s claim_draft_status=%s correlation_id=%s",
            batch_id,
            candidate_id,
            result.status.value,
            correlation_id,
        )


class ProcessScoringRequestHandler:
    def __init__(self, deps: ProcessScoringDeps) -> None:
        self._verdict_repo = deps.verdict_repo
        self._candidate_reader = deps.candidate_reader
        self._bundle_reader = deps.bundle_reader
        self._score_handler = deps.score_handler
        self._publisher = deps.publisher
        self._settings = deps.settings
        self._claim_draft_generator = deps.claim_draft_generator

    async def handle(self, envelope: EventEnvelope) -> ProcessOutcome:
        candidate_id = envelope.payload.get("candidate_id")
        job_id = envelope.payload.get("job_id", envelope.batch_id)
        if not candidate_id:
            logger.error(
                "batch_id=%s document_id=%s correlation_id=%s "
                "outcome=PERMANENT reason=missing_candidate_id",
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
                    reason="missing_candidate_id",
                )
            )
        try:
            return await self._execute(envelope, candidate_id)
        except Exception as exc:
            return await self._classify_and_log(exc, envelope, candidate_id, job_id)

    async def _execute(self, envelope: EventEnvelope, candidate_id: str) -> ProcessOutcome:
        existing = await self._verdict_repo.find_by_candidate(candidate_id, envelope.batch_id)
        if existing is not None:
            return await self._handle_idempotent(existing, envelope)
        return await self._score_and_store(envelope, candidate_id)

    async def _handle_idempotent(
        self, verdict: StoredVerdict, envelope: EventEnvelope
    ) -> ProcessOutcome:
        event = make_scoring_completed_event(verdict, envelope.correlation_id)
        await self._publisher.publish(self._settings.scoring_completed_topic, event)
        logger.info(
            "batch_id=%s document_id=%s candidate_id=%s correlation_id=%s "
            "outcome=SUCCESS reason=idempotent",
            envelope.batch_id,
            envelope.document_id,
            verdict.candidate_id,
            envelope.correlation_id,
        )
        return ProcessOutcome.SUCCESS

    async def _score_and_store(self, envelope: EventEnvelope, candidate_id: str) -> ProcessOutcome:
        evidence_id = envelope.payload.get("evidence_bundle_id")
        candidate = await self._candidate_reader.get_by_candidate_id(
            candidate_id, envelope.batch_id
        )
        bundle = await self._bundle_reader.get_domain_bundle(candidate_id, envelope.batch_id)
        if bundle is None:
            logger.warning(
                "batch_id=%s document_id=%s candidate_id=%s evidence_id=%s "
                "correlation_id=%s outcome=TRANSIENT reason=bundle_not_ready",
                envelope.batch_id,
                envelope.document_id,
                candidate_id,
                evidence_id,
                envelope.correlation_id,
            )
            return ProcessOutcome.TRANSIENT
        request = ScoreVerdictRequestDto(
            candidate_description=candidate.claim_text,
            bundle=bundle,
            correlation_id=envelope.correlation_id,
            ai_model=envelope.payload.get("ai_model"),
        )
        response = await self._score_handler.handle(request)
        await self._generate_claim_draft(
            candidate_id, envelope.batch_id, candidate, bundle, envelope
        )
        logger.info(
            "batch_id=%s document_id=%s candidate_id=%s verdict_id=%s "
            "correlation_id=%s outcome=SUCCESS",
            envelope.batch_id,
            envelope.document_id,
            candidate_id,
            response.verdict_id,
            envelope.correlation_id,
        )
        return ProcessOutcome.SUCCESS

    async def _generate_claim_draft(
        self, candidate_id: str, batch_id: str, candidate, bundle, envelope: EventEnvelope
    ) -> None:
        if self._claim_draft_generator is None:
            return
        try:
            draft_request = _build_draft_request(candidate, bundle, envelope)
            result = await self._claim_draft_generator.generate(draft_request)
            await _update_verdict_with_draft(
                self._verdict_repo, candidate_id, batch_id, result, envelope.correlation_id
            )
        except Exception as exc:
            logger.warning(
                "batch_id=%s candidate_id=%s claim_draft_failed error=%s correlation_id=%s",
                batch_id,
                candidate_id,
                type(exc).__name__,
                envelope.correlation_id,
            )

    async def _fail(self, context: FailureContext) -> ProcessOutcome:
        event = make_scoring_failed_event(
            batch_id=context.batch_id,
            document_id=context.document_id,
            reason=context.reason,
            job_id=context.job_id,
            candidate_id=context.candidate_id,
            trigger_type=_TRIGGER_TYPE,
            correlation_id=context.correlation_id,
        )
        await self._publisher.publish(self._settings.scoring_failed_topic, event)
        return ProcessOutcome.PERMANENT

    async def _classify_and_log(
        self, exc: Exception, envelope: EventEnvelope, candidate_id: str, job_id: str
    ) -> ProcessOutcome:
        outcome = _classify_error(exc)
        logger.error(
            "batch_id=%s document_id=%s candidate_id=%s correlation_id=%s outcome=%s error=%s",
            envelope.batch_id,
            envelope.document_id,
            candidate_id,
            envelope.correlation_id,
            outcome.value,
            exc,
        )
        if outcome == ProcessOutcome.PERMANENT:
            return await self._fail(
                FailureContext(
                    batch_id=envelope.batch_id,
                    document_id=envelope.document_id,
                    correlation_id=envelope.correlation_id,
                    candidate_id=candidate_id,
                    job_id=job_id,
                    reason=str(exc),
                )
            )
        return outcome
