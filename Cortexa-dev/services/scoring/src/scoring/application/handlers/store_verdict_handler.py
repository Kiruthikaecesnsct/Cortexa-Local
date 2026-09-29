import logging
from dataclasses import dataclass

from scoring.application.dtos.store_verdict_request import StoreVerdictRequestDto
from scoring.application.dtos.store_verdict_response import StoreVerdictResponseDto
from scoring.application.handlers.event_helpers import publish_verdict_event
from scoring.domain.events.scoring_completed import make_scoring_completed_event
from scoring.domain.models.build_verdict_request import BuildVerdictRequest
from scoring.domain.models.stored_verdict import StoredVerdict
from scoring.domain.repositories.storage_protocols import EventPublisher, VerdictRepository
from scoring.infrastructure.config.settings import ScoringSettings

_logger = logging.getLogger(__name__)


@dataclass
class StoreVerdictDeps:
    repository: VerdictRepository
    publisher: EventPublisher
    settings: ScoringSettings


class StoreVerdictHandler:
    def __init__(self, deps: StoreVerdictDeps) -> None:
        self._deps = deps

    async def handle(self, request: StoreVerdictRequestDto) -> StoreVerdictResponseDto:
        dual = request.dual_verdict
        primary = dual.primary
        verdict = StoredVerdict.build(
            BuildVerdictRequest(
                batch_id=primary.batch_id,
                job_id=primary.job_id,
                candidate_id=primary.candidate_id,
                document_id=primary.document_id,
                axes=primary.axes,
                agreement_level=dual.agreement_level,
                agreeing_axis_count=dual.agreeing_axis_count,
                grounding_meets_minimum=True,
                grounding_source_count=0,
            )
        )
        await self._deps.repository.save(verdict)
        event = make_scoring_completed_event(verdict, request.correlation_id)
        await publish_verdict_event(
            self._deps.publisher,
            self._deps.settings.scoring_completed_topic,
            verdict,
            event,
        )
        return StoreVerdictResponseDto(
            verdict_id=verdict.id,
            document_id=verdict.document_id,
            published=True,
        )
