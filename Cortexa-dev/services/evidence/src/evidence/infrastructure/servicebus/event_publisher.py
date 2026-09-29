from azure.servicebus import ServiceBusMessage
from azure.servicebus.aio import ServiceBusClient
from azure.servicebus.exceptions import ServiceBusError

from evidence.domain.errors.evidence_errors import EventPublishError
from evidence.domain.events.event_envelope import EventEnvelope


class ServiceBusEventPublisher:
    def __init__(self, client: ServiceBusClient) -> None:
        self._client = client

    async def publish(self, topic: str, event: EventEnvelope) -> None:
        body = event.model_dump_json().encode()
        message = ServiceBusMessage(
            body=body,
            session_id=event.batch_id,
            correlation_id=event.correlation_id,
            content_type="application/json",
        )
        try:
            async with self._client.get_topic_sender(topic_name=topic) as sender:
                await sender.send_messages(message)
        except ServiceBusError as exc:
            raise EventPublishError(str(exc)) from exc
