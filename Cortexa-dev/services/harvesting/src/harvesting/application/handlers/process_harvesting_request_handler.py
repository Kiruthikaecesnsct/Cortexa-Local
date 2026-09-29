import logging
from dataclasses import dataclass
from datetime import UTC, datetime
from enum import Enum

from harvesting.application.candidate_assembler import ChunkGeometryRepos, assemble_candidates
from harvesting.application.dtos.assemble_request import AssembleRequestDto
from harvesting.application.handlers.assemble_handler import AssembleHandler
from harvesting.domain.errors.harvesting_errors import (
    AxisMissingError,
    EventPublishError,
    ReportNotFoundError,
    StoragePermanentError,
    StorageWriteError,
)
from harvesting.domain.events.event_envelope import EventEnvelope
from harvesting.domain.events.harvesting_failed import make_harvesting_failed_event
from harvesting.domain.models.engine_completed_event import EngineCompletedEvent
from harvesting.domain.models.harvesting_report import HarvestingReport
from harvesting.domain.models.rank_weights import RankWeights
from harvesting.domain.repositories.harvesting_protocols import (
    CandidateReadRepository,
    EventPublisher,
    EvidenceBundleReadRepository,
    FailedEventPublisher,
    ReportRepository,
    VerdictReadRepository,
)
from harvesting.infrastructure.config.settings import HarvestingSettings

logger = logging.getLogger(__name__)

_PERMANENT_ERRORS = (AxisMissingError, StoragePermanentError)
_TRANSIENT_ERRORS = (StorageWriteError, EventPublishError)
_TRIGGER_TYPE = "pipeline"


class ProcessOutcome(Enum):
    SUCCESS = "success"
    PERMANENT = "permanent"
    TRANSIENT = "transient"


@dataclass
class ProcessHarvestingRequestDeps:
    candidate_repo: CandidateReadRepository
    verdict_repo: VerdictReadRepository
    evidence_repo: EvidenceBundleReadRepository
    report_repo: ReportRepository
    publisher: EventPublisher
    failed_publisher: FailedEventPublisher
    assemble_handler: AssembleHandler
    weights: RankWeights
    settings: HarvestingSettings
    geometry_repos: ChunkGeometryRepos | None = None


class ProcessHarvestingRequestHandler:
    def __init__(self, deps: ProcessHarvestingRequestDeps) -> None:
        self._deps = deps

    async def handle(self, envelope: EventEnvelope) -> ProcessOutcome:
        batch_id = envelope.batch_id
        try:
            return await self._execute(envelope, batch_id)
        except Exception as exc:
            return await self._classify_error(exc, envelope, batch_id)

    async def _execute(self, envelope: EventEnvelope, batch_id: str) -> ProcessOutcome:
        try:
            existing = await self._deps.report_repo.get_by_batch(batch_id)
            return await self._handle_idempotent(existing, envelope)
        except ReportNotFoundError:
            pass
        return await self._process_new(envelope, batch_id)

    async def _handle_idempotent(
        self, report: HarvestingReport, envelope: EventEnvelope
    ) -> ProcessOutcome:
        event = EngineCompletedEvent(
            batch_id=report.batch_id,
            document_id=report.document_id,
            correlation_id=envelope.correlation_id or "",
            occurred_at=datetime.now(UTC),
            payload={
                "document_id": report.document_id,
                "engine": "harvesting",
                "report_id": report.id,
            },
        )
        await self._deps.publisher.publish(event)
        logger.info(
            "batch_id=%s correlation_id=%s report_id=%s outcome=SUCCESS reason=idempotent",
            report.batch_id,
            envelope.correlation_id,
            report.id,
        )
        return ProcessOutcome.SUCCESS

    async def _process_new(self, envelope: EventEnvelope, batch_id: str) -> ProcessOutcome:
        candidates = await self._deps.candidate_repo.get_by_batch(batch_id)
        if not candidates:
            logger.error(
                "batch_id=%s correlation_id=%s outcome=PERMANENT reason=no_candidates",
                batch_id,
                envelope.correlation_id,
            )
            return await self._fail(
                batch_id, envelope.document_id, envelope.correlation_id, "no_candidates"
            )

        verdicts = await self._deps.verdict_repo.get_by_batch(batch_id)
        evidence_bundles = await self._fetch_evidence_bundles(batch_id, envelope.correlation_id)
        evidence_by_candidate = self._index_evidence_by_candidate(evidence_bundles)
        document_id = self._resolve_document_id(envelope, candidates)
        assembled = await assemble_candidates(
            candidates,
            verdicts,
            self._deps.weights,
            self._deps.settings,
            evidence_by_candidate,
            self._deps.geometry_repos,
        )

        if not assembled:
            logger.error(
                "batch_id=%s correlation_id=%s outcome=PERMANENT reason=no_assembled_candidates",
                batch_id,
                envelope.correlation_id,
            )
            return await self._fail(
                batch_id, document_id, envelope.correlation_id, "no_assembled_candidates"
            )

        request = AssembleRequestDto(
            batch_id=batch_id,
            document_id=document_id,
            correlation_id=envelope.correlation_id or "",
            candidates=assembled,
        )
        response = await self._deps.assemble_handler.handle(request)
        logger.info(
            "batch_id=%s correlation_id=%s report_id=%s outcome=SUCCESS",
            batch_id,
            envelope.correlation_id,
            response.report_id,
        )
        return ProcessOutcome.SUCCESS

    async def _fetch_evidence_bundles(
        self, batch_id: str, correlation_id: str | None
    ) -> list[dict]:
        try:
            return await self._deps.evidence_repo.get_by_batch(batch_id)
        except Exception as exc:
            logger.warning(
                "batch_id=%s correlation_id=%s reason=evidence_bundle_read_failed error=%s",
                batch_id,
                correlation_id,
                exc,
            )
            return []

    def _index_evidence_by_candidate(self, bundles: list[dict]) -> dict[str, dict]:
        return {bundle["candidate_id"]: bundle for bundle in bundles if "candidate_id" in bundle}

    def _resolve_document_id(self, envelope: EventEnvelope, candidates: list[dict]) -> str:
        if envelope.document_id:
            return envelope.document_id
        if candidates and candidates[0].get("document_id"):
            return str(candidates[0]["document_id"])
        return ""

    async def _fail(
        self,
        batch_id: str,
        document_id: str | None,
        correlation_id: str | None,
        reason: str,
    ) -> ProcessOutcome:
        event = make_harvesting_failed_event(
            batch_id=batch_id,
            document_id=document_id,
            reason=reason,
            job_id=batch_id,
            trigger_type=_TRIGGER_TYPE,
            correlation_id=correlation_id,
        )
        await self._deps.failed_publisher.publish(
            self._deps.settings.harvesting_failed_topic, event
        )
        return ProcessOutcome.PERMANENT

    async def _classify_error(
        self, exc: Exception, envelope: EventEnvelope, batch_id: str
    ) -> ProcessOutcome:
        outcome = (
            ProcessOutcome.PERMANENT
            if isinstance(exc, _PERMANENT_ERRORS)
            else ProcessOutcome.TRANSIENT
        )
        logger.error(
            "batch_id=%s correlation_id=%s outcome=%s error=%s",
            batch_id,
            envelope.correlation_id,
            outcome.value,
            exc,
        )
        if outcome == ProcessOutcome.PERMANENT:
            return await self._fail(
                batch_id, envelope.document_id, envelope.correlation_id, str(exc)
            )
        return outcome
