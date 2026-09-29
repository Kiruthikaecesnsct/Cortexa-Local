from azure.servicebus import ServiceBusMessage
from azure.servicebus.aio import ServiceBusClient

from ingestion.domain.events.event_envelope import EventEnvelope


class EventPublisher:
    def __init__(self, client: ServiceBusClient) -> None:
        self._client = client

    async def publish(self, topic: str, event: EventEnvelope) -> None:
        body = event.model_dump_json()
        message = ServiceBusMessage(
            body=body,
            subject=topic,
            correlation_id=event.correlation_id,
            content_type="application/json",
            session_id=event.batch_id,
        )
        async with self._client.get_topic_sender(topic_name=topic) as sender:
            await sender.send_messages(message)
