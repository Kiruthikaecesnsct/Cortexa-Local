import logging

from scoring.domain.errors.storage_errors import EventPublishError
from scoring.domain.models.stored_verdict import StoredVerdict
from scoring.domain.repositories.storage_protocols import EventPublisher

_logger = logging.getLogger(__name__)


async def publish_verdict_event(
    publisher: EventPublisher,
    topic: str,
    verdict: StoredVerdict,
    event: object,
) -> None:
    try:
        await publisher.publish(topic, event)
    except Exception as exc:
        _logger.warning("Event publish failed after verdict save: %s", exc)
        if isinstance(exc, EventPublishError):
            raise
        raise EventPublishError(
            str(exc),
            verdict_id=verdict.id,
            document_id=str(verdict.document_id),
        ) from exc
