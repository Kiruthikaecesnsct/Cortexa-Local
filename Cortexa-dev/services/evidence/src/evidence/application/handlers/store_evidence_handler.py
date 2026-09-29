import logging
from dataclasses import dataclass

from evidence.application.dtos.store_evidence_request import StoreEvidenceRequestDto
from evidence.application.dtos.store_evidence_response import StoreEvidenceResponseDto
from evidence.domain.errors.evidence_errors import EventPublishError
from evidence.domain.events.evidence_completed import make_evidence_completed_event
from evidence.domain.repositories.storage_protocols import EventPublisher, EvidenceBundleRepository
from evidence.infrastructure.config.settings import EvidenceSettings

_logger = logging.getLogger(__name__)


@dataclass
class StoreEvidenceDeps:
    repository: EvidenceBundleRepository
    publisher: EventPublisher
    settings: EvidenceSettings


class StoreEvidenceHandler:
    def __init__(self, deps: StoreEvidenceDeps) -> None:
        self._deps = deps

    async def handle(self, request: StoreEvidenceRequestDto) -> StoreEvidenceResponseDto:
        bundle = request.bundle
        await self._deps.repository.save(bundle)
        event = make_evidence_completed_event(bundle, request.correlation_id)
        published = await self._publish(event)
        return StoreEvidenceResponseDto(
            bundle_id=bundle.id,
            document_id=bundle.document_id,
            published=published,
        )

    async def _publish(self, event) -> bool:
        try:
            await self._deps.publisher.publish(self._deps.settings.evidence_completed_topic, event)
            return True
        except EventPublishError as exc:
            _logger.warning("Event publish failed after bundle save: %s", exc)
            return False
