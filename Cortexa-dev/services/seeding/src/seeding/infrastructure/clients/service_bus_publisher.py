import json
import logging

from azure.servicebus import ServiceBusMessage
from azure.servicebus.aio import ServiceBusClient

from seeding.domain.errors.seeding_errors import EventPublishError

_logger = logging.getLogger(__name__)


class ServiceBusPublisher:
    def __init__(self, client: ServiceBusClient) -> None:
        self._client = client

    async def publish(
        self,
        topic: str,
        payload: dict,
        session_id: str | None = None,
        correlation_id: str | None = None,
    ) -> None:
        body = json.dumps(payload).encode()
        message = ServiceBusMessage(
            body=body,
            session_id=session_id,
            correlation_id=correlation_id,
            content_type="application/json",
        )
        try:
            async with self._client.get_topic_sender(topic_name=topic) as sender:
                await sender.send_messages(message)
        except Exception as exc:
            raise EventPublishError(str(exc)) from exc
        _logger.info("published event to topic %s", topic)
