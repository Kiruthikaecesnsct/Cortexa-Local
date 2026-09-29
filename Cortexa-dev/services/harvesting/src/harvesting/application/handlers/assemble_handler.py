import uuid
from dataclasses import dataclass
from datetime import UTC, datetime

from harvesting.application.dtos.assemble_request import AssembleRequestDto
from harvesting.application.dtos.assemble_response import AssembleResponseDto
from harvesting.domain.enums.scoring_axis import ScoringAxis
from harvesting.domain.errors.harvesting_errors import AxisMissingError
from harvesting.domain.models.engine_completed_event import EngineCompletedEvent
from harvesting.domain.models.harvesting_report import HarvestingReport
from harvesting.domain.models.report_candidate import ReportCandidate
from harvesting.domain.repositories.harvesting_protocols import EventPublisher, ReportRepository

_ALL_AXES = set(ScoringAxis)


@dataclass
class AssembleDeps:
    report_repo: ReportRepository
    publisher: EventPublisher


class AssembleHandler:
    def __init__(self, deps: AssembleDeps) -> None:
        self._deps = deps

    async def handle(self, request: AssembleRequestDto) -> AssembleResponseDto:
        for candidate in request.candidates:
            missing = _ALL_AXES - set(candidate.axes.keys())
            if missing:
                raise AxisMissingError(next(iter(missing)).value)

        report_id = str(uuid.uuid4())
        report_candidates = [
            ReportCandidate(
                candidate_id=c.candidate_id,
                title=c.title,
                description=c.description,
                claim_text=c.claim_text,
                claim_draft=c.claim_draft,
                maturity=c.maturity,
                rank=c.rank,
                weighted_score=c.weighted_score,
                axes=c.axes,
                agreement_flag=c.agreement_flag,
                citations=c.citations,
                provenance_links=c.provenance_links,
                source_availability=c.source_availability,
                source_status=c.source_status,
                evidence_sources=c.evidence_sources,
            )
            for c in request.candidates
        ]

        report = HarvestingReport(
            id=report_id,
            batch_id=request.batch_id,
            document_id=request.document_id,
            generated_at=datetime.now(UTC),
            candidates=report_candidates,
        )

        await self._deps.report_repo.save(report)

        event = EngineCompletedEvent(
            batch_id=request.batch_id,
            document_id=request.document_id,
            correlation_id=request.correlation_id,
            occurred_at=datetime.now(UTC),
            payload={
                "document_id": request.document_id,
                "engine": "harvesting",
                "report_id": report_id,
            },
        )

        await self._deps.publisher.publish(event)

        return AssembleResponseDto(
            report_id=report_id,
            candidate_count=len(request.candidates),
        )
