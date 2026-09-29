import logging
from dataclasses import dataclass
from datetime import UTC, datetime
from uuid import uuid4

from pydantic import ValidationError

from seeding.application.handlers.process_seeding_request_handler import ProcessOutcome
from seeding.application.report.seeded_report_assembler import (
    assemble_opportunities,
    build_concept_map,
)
from seeding.domain.errors.seeding_errors import OpportunityParseError, SeedingReportNotFoundError
from seeding.domain.events.event_envelope import EventEnvelope
from seeding.domain.models.landscape import LandscapeProvenance, PriorArtLandscape
from seeding.domain.models.seeding_result import SeedingResult
from seeding.infrastructure.config.settings import SeedingSettings
from seeding.infrastructure.cosmos.candidate_read_repository import CosmosCandidateReadRepository
from seeding.infrastructure.cosmos.evidence_bundle_read_repository import (
    CosmosEvidenceBundleReadRepository,
)
from seeding.infrastructure.cosmos.landscape_repository import LandscapeRepository
from seeding.infrastructure.cosmos.seeding_report_repository import SeedingReportRepository
from seeding.infrastructure.cosmos.verdict_read_repository import CosmosVerdictReadRepository

logger = logging.getLogger(__name__)

_PERMANENT_ERRORS = (OpportunityParseError, ValidationError)


@dataclass
class ProcessSeedingReportRequestDeps:
    candidate_repo: CosmosCandidateReadRepository
    verdict_repo: CosmosVerdictReadRepository
    evidence_repo: CosmosEvidenceBundleReadRepository
    report_repo: SeedingReportRepository
    landscape_repo: LandscapeRepository
    publisher: object
    settings: SeedingSettings


class ProcessSeedingReportRequestHandler:
    def __init__(self, deps: ProcessSeedingReportRequestDeps) -> None:
        self._deps = deps

    async def handle(self, envelope: EventEnvelope) -> ProcessOutcome:
        try:
            return await self._execute(envelope)
        except Exception as exc:
            return self._classify_error(exc, envelope)

    async def _execute(self, envelope: EventEnvelope) -> ProcessOutcome:
        batch_id = envelope.batch_id
        try:
            existing = await self._deps.report_repo.get_by_batch(batch_id)
            await self._publish_completed(existing.document_id, existing.id, envelope)
            logger.info(
                "batch_id=%s correlation_id=%s report_id=%s outcome=SUCCESS reason=idempotent",
                batch_id,
                envelope.correlation_id,
                existing.id,
            )
            return ProcessOutcome.SUCCESS
        except SeedingReportNotFoundError:
            pass
        return await self._build_report(envelope)

    async def _build_report(self, envelope: EventEnvelope) -> ProcessOutcome:
        batch_id = envelope.batch_id
        candidates = await self._deps.candidate_repo.get_seeded_by_batch(batch_id)
        candidate_ids = {candidate["id"] for candidate in candidates if "id" in candidate}
        verdicts = await self._deps.verdict_repo.get_for_candidates(batch_id, candidate_ids)
        bundles = await self._deps.evidence_repo.get_for_candidates(batch_id, candidate_ids)

        document_id = envelope.document_id or self._first_document_id(candidates)
        landscape = await self._load_landscape(batch_id, document_id)
        opportunities = assemble_opportunities(candidates, verdicts, bundles, landscape)
        result = self._make_result(batch_id, document_id, opportunities)
        result.concept_map = build_concept_map(opportunities, landscape)
        result.landscape = self._landscape_provenance(landscape)
        await self._deps.report_repo.save(result)
        await self._publish_completed(document_id, result.id, envelope)
        logger.info(
            "batch_id=%s correlation_id=%s report_id=%s opportunities=%d outcome=SUCCESS",
            batch_id,
            envelope.correlation_id,
            result.id,
            len(opportunities),
        )
        return ProcessOutcome.SUCCESS

    @staticmethod
    def _first_document_id(candidates: list[dict]) -> str:
        for candidate in candidates:
            document_id = candidate.get("document_id")
            if document_id:
                return document_id
        return ""

    @staticmethod
    def _make_result(batch_id: str, document_id: str, opportunities: list) -> SeedingResult:
        return SeedingResult(
            id=f"seeding-report-{batch_id}",
            batch_id=batch_id,
            document_id=document_id,
            engine="seeding",
            seeding_mode="deep",
            opportunities=opportunities,
            is_empty=not opportunities,
        )

    async def _load_landscape(self, batch_id: str, document_id: str) -> PriorArtLandscape | None:
        if not document_id:
            return None
        try:
            return await self._deps.landscape_repo.get_landscape(
                batch_id, document_id, self._deps.settings.landscape_schema_version
            )
        except Exception as exc:
            logger.warning(
                "batch_id=%s outcome=DEGRADE reason=landscape_load_failed error_class=%s",
                batch_id,
                type(exc).__name__,
            )
            return None

    @staticmethod
    def _landscape_provenance(landscape: PriorArtLandscape | None) -> LandscapeProvenance | None:
        if landscape is None:
            return None
        return LandscapeProvenance(
            landscape_id=landscape.id,
            schema_version=landscape.schema_version,
            source_flags=landscape.source_flags,
        )

    async def _publish_completed(
        self, document_id: str, report_id: str, envelope: EventEnvelope
    ) -> None:
        event = {
            "event_id": str(uuid4()),
            "schema_version": "1.0",
            "event_type": "engine.completed",
            "batch_id": envelope.batch_id,
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
            session_id=envelope.batch_id,
            correlation_id=envelope.correlation_id,
        )

    def _classify_error(self, exc: Exception, envelope: EventEnvelope) -> ProcessOutcome:
        outcome = (
            ProcessOutcome.PERMANENT
            if isinstance(exc, _PERMANENT_ERRORS)
            else ProcessOutcome.TRANSIENT
        )
        logger.error(
            "batch_id=%s correlation_id=%s outcome=%s error_class=%s",
            envelope.batch_id,
            envelope.correlation_id,
            outcome.value,
            type(exc).__name__,
        )
        return outcome
