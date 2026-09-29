import logging
import time
from dataclasses import dataclass
from datetime import UTC, datetime
from enum import Enum
from uuid import uuid4

from pydantic import ValidationError

from seeding.application.ideation.deep_seeding_generation import generate_deep_seeding
from seeding.application.ideation.round_engine import (
    IdeationContext,
    RoundEngine,
    RoundEngineDeps,
)
from seeding.application.ideation.seeded_candidate_mapper import map_accepted_ideas
from seeding.application.seeding_generation import generate_seeding_opportunities
from seeding.domain.errors.seeding_errors import (
    ModelRouterFailedError,
    OpportunityParseError,
    SeedingReportNotFoundError,
    UngroundedSeedingError,
)
from seeding.domain.events.event_envelope import EventEnvelope
from seeding.domain.events.ideation_completed import make_ideation_completed_event
from seeding.domain.events.seeding_failed import make_seeding_failed_event
from seeding.domain.models.landscape import LandscapeProvenance
from seeding.domain.models.seeding_result import SeedingResult
from seeding.domain.ports.candidate_read_port import CandidateReadPort
from seeding.domain.ports.candidate_write_port import CandidateWritePort
from seeding.domain.ports.chunk_read_port import ChunkReadPort
from seeding.domain.ports.event_publisher_port import EventPublisherPort
from seeding.domain.ports.model_router_port import ModelRouterPort
from seeding.domain.ports.seeding_report_repository_port import SeedingReportRepositoryPort
from seeding.domain.ports.vector_router_port import VectorRouterPort
from seeding.infrastructure.config.settings import SeedingSettings
from seeding.infrastructure.cosmos.digest_repository import DigestRepository
from seeding.infrastructure.cosmos.landscape_repository import LandscapeRepository
from seeding.infrastructure.cosmos.scratchpad_repository import ScratchpadRepository

logger = logging.getLogger(__name__)

_PERMANENT_ERRORS = (UngroundedSeedingError, OpportunityParseError, ValidationError)
_TRIGGER_TYPE = "pipeline"
_DEEP_MODE = "deep"


class ProcessOutcome(Enum):
    SUCCESS = "success"
    PERMANENT = "permanent"
    TRANSIENT = "transient"


@dataclass
class ProcessSeedingRequestDeps:
    candidate_repo: CandidateReadPort
    report_repo: SeedingReportRepositoryPort
    landscape_repo: LandscapeRepository
    publisher: EventPublisherPort
    client: ModelRouterPort
    settings: SeedingSettings
    chunk_repo: ChunkReadPort | None = None
    digest_repo: DigestRepository | None = None
    scratchpad_repo: ScratchpadRepository | None = None
    vector_client: VectorRouterPort | None = None
    ideation_client: ModelRouterPort | None = None
    candidate_write_repo: CandidateWritePort | None = None


class ProcessSeedingRequestHandler:
    def __init__(self, deps: ProcessSeedingRequestDeps) -> None:
        self._deps = deps

    async def handle(self, envelope: EventEnvelope) -> ProcessOutcome:
        batch_id = envelope.batch_id
        try:
            return await self._execute(envelope, batch_id)
        except Exception as exc:
            return await self._classify_error(
                exc, batch_id, envelope.document_id, envelope.correlation_id
            )

    async def _execute(self, envelope: EventEnvelope, batch_id: str) -> ProcessOutcome:
        try:
            existing = await self._deps.report_repo.get_by_batch(batch_id)
            return await self._handle_idempotent(existing, envelope)
        except SeedingReportNotFoundError:
            pass
        return await self._process_new(envelope, batch_id)

    async def _handle_idempotent(
        self, report: SeedingResult, envelope: EventEnvelope
    ) -> ProcessOutcome:
        await self._publish_completed(report.batch_id, report.document_id, report.id, envelope)
        logger.info(
            "batch_id=%s correlation_id=%s report_id=%s outcome=SUCCESS reason=idempotent",
            report.batch_id,
            envelope.correlation_id,
            report.id,
        )
        return ProcessOutcome.SUCCESS

    async def _process_new(self, envelope: EventEnvelope, batch_id: str) -> ProcessOutcome:
        mode = envelope.payload.seeding_mode or self._deps.settings.seeding_mode_default
        if mode == _DEEP_MODE:
            return await self._process_deep(envelope, batch_id)
        roadmap_context = envelope.payload.roadmap_context
        ai_model = envelope.payload.ai_model
        candidates = await self._deps.candidate_repo.get_by_batch(batch_id)
        result = await generate_seeding_opportunities(
            candidates, roadmap_context, self._deps.client, self._deps.settings, ai_model
        )
        result.seeding_mode = mode
        document_id = result.document_id or envelope.document_id or ""
        result.landscape = await self._load_landscape_provenance(batch_id, document_id)
        await self._deps.report_repo.save(result)
        await self._publish_completed(batch_id, document_id, result.id, envelope)
        logger.info(
            "batch_id=%s correlation_id=%s report_id=%s outcome=SUCCESS",
            batch_id,
            envelope.correlation_id,
            result.id,
        )
        return ProcessOutcome.SUCCESS

    def _build_ideation_context(self, envelope: EventEnvelope, batch_id: str) -> IdeationContext:
        budget = self._deps.settings.ideation_message_time_budget_seconds
        return IdeationContext(
            batch_id=batch_id,
            document_id=envelope.document_id or "",
            correlation_id=envelope.correlation_id,
            ai_model=envelope.payload.ai_model,
            roadmap_context=envelope.payload.roadmap_context,
            deadline=time.monotonic() + budget,
        )

    def _build_round_engine(self) -> RoundEngine:
        deps = self._deps
        return RoundEngine(
            RoundEngineDeps(
                chunk_repo=deps.chunk_repo,
                digest_repo=deps.digest_repo,
                landscape_repo=deps.landscape_repo,
                scratchpad_repo=deps.scratchpad_repo,
                vector_client=deps.vector_client,
                client=deps.ideation_client or deps.client,
                settings=deps.settings,
            )
        )

    async def _process_deep(self, envelope: EventEnvelope, batch_id: str) -> ProcessOutcome:
        ctx = self._build_ideation_context(envelope, batch_id)
        outcome = await generate_deep_seeding(self._build_round_engine(), ctx)
        if outcome.resume_needed:
            logger.info(
                "batch_id=%s correlation_id=%s outcome=TRANSIENT reason=ideation_resume",
                batch_id,
                envelope.correlation_id,
            )
            return ProcessOutcome.TRANSIENT
        if outcome.accepted_ideas:
            return await self._persist_seeded_candidates(outcome.accepted_ideas, ctx, envelope)
        return await self._save_empty_report(outcome.result, ctx, envelope)

    async def _save_empty_report(
        self, result: SeedingResult, ctx: IdeationContext, envelope: EventEnvelope
    ) -> ProcessOutcome:
        batch_id = ctx.batch_id
        result.seeding_mode = _DEEP_MODE
        result.landscape = await self._load_landscape_provenance(batch_id, ctx.document_id)
        await self._deps.report_repo.save(result)
        await self._publish_completed(batch_id, ctx.document_id, result.id, envelope)
        logger.info(
            "batch_id=%s correlation_id=%s report_id=%s is_empty=%s outcome=SUCCESS",
            batch_id,
            envelope.correlation_id,
            result.id,
            result.is_empty,
        )
        return ProcessOutcome.SUCCESS

    async def _persist_seeded_candidates(
        self, ideas: list, ctx: IdeationContext, envelope: EventEnvelope
    ) -> ProcessOutcome:
        batch_id = ctx.batch_id
        candidates = map_accepted_ideas(ideas, batch_id, ctx.document_id)
        await self._deps.candidate_write_repo.upsert_many(candidates)
        candidate_ids = [candidate["id"] for candidate in candidates]
        event = make_ideation_completed_event(
            batch_id, ctx.document_id, candidate_ids, envelope.correlation_id
        )
        await self._deps.publisher.publish(
            self._deps.settings.ideation_completed_topic,
            event,
            session_id=batch_id,
            correlation_id=envelope.correlation_id,
        )
        logger.info(
            "batch_id=%s correlation_id=%s candidate_count=%d outcome=SUCCESS "
            "reason=ideation_completed",
            batch_id,
            envelope.correlation_id,
            len(candidate_ids),
        )
        return ProcessOutcome.SUCCESS

    async def _load_landscape_provenance(
        self, batch_id: str, document_id: str
    ) -> LandscapeProvenance | None:
        if not document_id:
            return None
        landscape = await self._deps.landscape_repo.get_landscape(
            batch_id, document_id, self._deps.settings.landscape_schema_version
        )
        if landscape is None:
            return None
        return LandscapeProvenance(
            landscape_id=landscape.id,
            schema_version=landscape.schema_version,
            source_flags=landscape.source_flags,
        )

    async def _publish_completed(
        self, batch_id: str, document_id: str, report_id: str, envelope: EventEnvelope
    ) -> None:
        event = {
            "event_id": str(uuid4()),
            "schema_version": "1.0",
            "event_type": "engine.completed",
            "batch_id": batch_id,
            "document_id": document_id,
            "correlation_id": envelope.correlation_id,
            "occurred_at": datetime.now(UTC).isoformat(),
            "payload": {
                "document_id": document_id,
                "engine": self._deps.settings.engine_name,
                "report_id": report_id,
            },
        }
        await self._deps.publisher.publish(
            self._deps.settings.engine_completed_topic,
            event,
            session_id=batch_id,
            correlation_id=envelope.correlation_id,
        )

    async def _classify_error(
        self,
        exc: Exception,
        batch_id: str,
        document_id: str | None,
        correlation_id: str | None,
    ) -> ProcessOutcome:
        if isinstance(exc, ModelRouterFailedError):
            outcome = self._classify_model_error(exc, batch_id, correlation_id)
        else:
            outcome = (
                ProcessOutcome.PERMANENT
                if isinstance(exc, _PERMANENT_ERRORS)
                else ProcessOutcome.TRANSIENT
            )
            logger.error(
                "batch_id=%s correlation_id=%s outcome=%s error_class=%s",
                batch_id,
                correlation_id,
                outcome.value,
                type(exc).__name__,
            )
        if outcome != ProcessOutcome.PERMANENT:
            return outcome
        return await self._fail(batch_id, document_id, correlation_id, str(exc))

    def _classify_model_error(
        self, exc: ModelRouterFailedError, batch_id: str, correlation_id: str | None
    ) -> ProcessOutcome:
        sc = exc.status_code
        if sc is None or sc == 429 or 500 <= sc <= 599:
            outcome = ProcessOutcome.TRANSIENT
        else:
            outcome = ProcessOutcome.PERMANENT
        logger.error(
            "batch_id=%s correlation_id=%s outcome=%s status_code=%s error_class=%s",
            batch_id,
            correlation_id,
            outcome.value,
            sc,
            "ModelRouterFailedError",
        )
        return outcome

    async def _fail(
        self,
        batch_id: str,
        document_id: str | None,
        correlation_id: str | None,
        reason: str,
    ) -> ProcessOutcome:
        event = make_seeding_failed_event(
            batch_id=batch_id,
            document_id=document_id,
            reason=reason,
            job_id=batch_id,
            trigger_type=_TRIGGER_TYPE,
            correlation_id=correlation_id,
        )
        await self._deps.publisher.publish(
            self._deps.settings.seeding_failed_topic,
            event,
            session_id=batch_id,
            correlation_id=correlation_id,
        )
        return ProcessOutcome.PERMANENT
