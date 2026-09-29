"""RabbitMQ implementation of the Service Bus client surface this service uses.

Selected with MESSAGING_BACKEND=rabbitmq so the service can run against a
natively installed broker. It exposes the same get_topic_sender /
get_subscription_receiver / settle calls the Service Bus publishers and
consumers already make, so they stay unchanged.

Mapping: a Service Bus topic is a durable fanout exchange with the same name,
a subscription is the quorum queue "{topic}.{subscription}" (single active
consumer, which keeps per-queue ordering in place of sessions), and its
dead-letter sub-queue is the classic queue "{topic}.{subscription}.dlq".
The topology is declared once by deploy/local/native/rabbitmq-topology.ps1;
this module only looks entities up and fails fast when they are missing.
"""

import asyncio
from collections.abc import AsyncIterator, Iterable, Iterator
from contextlib import contextmanager
from dataclasses import dataclass
from typing import Any

import aio_pika
from aio_pika.abc import (
    AbstractChannel,
    AbstractIncomingMessage,
    AbstractQueueIterator,
    AbstractRobustConnection,
)
from aio_pika.exceptions import AMQPError
from azure.servicebus.exceptions import ServiceBusError

DEAD_LETTER_QUEUE_SUFFIX = ".dlq"
SESSION_ID_HEADER = "session_id"
DEAD_LETTER_REASON_HEADER = "dead_letter_reason"
DEAD_LETTER_DESCRIPTION_HEADER = "dead_letter_error_description"

_DELIVERY_COUNT_HEADER = "x-delivery-count"
_PREFETCH_COUNT = 1
_MAX_HEADER_TEXT_CHARS = 4096
_BROKER_ERRORS = (AMQPError, ConnectionError, TimeoutError)


class RabbitMqBrokerError(ServiceBusError):
    """Broker failure raised as a ServiceBusError so existing retry loops handle it."""


def subscription_queue_name(topic: str, subscription: str) -> str:
    return f"{topic}.{subscription}"


@contextmanager
def _translate_broker_errors() -> Iterator[None]:
    try:
        yield
    except _BROKER_ERRORS as exc:
        raise RabbitMqBrokerError(str(exc) or type(exc).__name__) from exc


def _to_bytes(body: Any) -> bytes:
    if isinstance(body, (bytes, bytearray)):
        return bytes(body)
    if isinstance(body, str):
        return body.encode("utf-8")
    return b"".join(_to_bytes(chunk) for chunk in body)


def _as_text(value: Any) -> str:
    return value.decode("utf-8") if isinstance(value, (bytes, bytearray)) else str(value)


def _header_text(headers: dict[str, Any], key: str) -> str | None:
    value = headers.get(key)
    return None if value is None else _as_text(value)


def _application_headers(properties: dict[Any, Any] | None) -> dict[str, Any]:
    return {_as_text(key): value for key, value in (properties or {}).items()}


def _to_amqp_message(message: Any) -> aio_pika.Message:
    headers = _application_headers(message.application_properties)
    if message.session_id:
        headers[SESSION_ID_HEADER] = message.session_id
    return aio_pika.Message(
        body=_to_bytes(message.body),
        headers=headers,
        content_type=message.content_type,
        correlation_id=message.correlation_id,
        message_id=message.message_id,
        type=message.subject,
        delivery_mode=aio_pika.DeliveryMode.PERSISTENT,
    )


@dataclass(frozen=True)
class RabbitMqReceivedMessage:
    body: bytes
    delivery_count: int
    session_id: str | None
    correlation_id: str | None
    application_properties: dict[str, Any]
    raw: AbstractIncomingMessage


def _to_received(raw: AbstractIncomingMessage) -> RabbitMqReceivedMessage:
    headers = dict(raw.headers or {})
    previous_deliveries = int(headers.get(_DELIVERY_COUNT_HEADER) or 0)
    return RabbitMqReceivedMessage(
        body=raw.body,
        delivery_count=previous_deliveries + 1,
        session_id=_header_text(headers, SESSION_ID_HEADER),
        correlation_id=raw.correlation_id,
        application_properties=headers,
        raw=raw,
    )


def _dead_letter_copy(
    message: RabbitMqReceivedMessage, reason: str | None, description: str | None
) -> aio_pika.Message:
    headers = {
        **message.application_properties,
        DEAD_LETTER_REASON_HEADER: (reason or "")[:_MAX_HEADER_TEXT_CHARS],
        DEAD_LETTER_DESCRIPTION_HEADER: (description or "")[:_MAX_HEADER_TEXT_CHARS],
    }
    return aio_pika.Message(
        body=message.body,
        headers=headers,
        content_type=message.raw.content_type,
        correlation_id=message.correlation_id,
        message_id=message.raw.message_id,
        delivery_mode=aio_pika.DeliveryMode.PERSISTENT,
    )


class _RabbitMqTopicSender:
    def __init__(self, client: RabbitMqServiceBusClient, topic: str) -> None:
        self._client = client
        self._topic = topic

    async def __aenter__(self) -> _RabbitMqTopicSender:
        return self

    async def __aexit__(self, *_exc_info: object) -> None:
        return None

    async def send_messages(self, message: Any) -> None:
        messages = message if isinstance(message, list) else [message]
        await self._client.publish(self._topic, messages)


class _RabbitMqSubscriptionReceiver:
    # RabbitMQ holds no session lock, so there is nothing for AutoLockRenewer to renew.
    session = None

    def __init__(self, client: RabbitMqServiceBusClient, queue_name: str) -> None:
        self._client = client
        self._queue_name = queue_name
        self._channel: AbstractChannel | None = None
        self._iterator: AbstractQueueIterator | None = None

    async def __aenter__(self) -> _RabbitMqSubscriptionReceiver:
        with _translate_broker_errors():
            self._channel = await self._client.open_consumer_channel()
            queue = await self._channel.get_queue(self._queue_name, ensure=True)
            self._iterator = queue.iterator()
        return self

    async def __aexit__(self, *_exc_info: object) -> None:
        if self._iterator is not None:
            await self._iterator.close()
        if self._channel is not None and not self._channel.is_closed:
            await self._channel.close()

    def __aiter__(self) -> AsyncIterator[RabbitMqReceivedMessage]:
        return self._messages()

    async def _messages(self) -> AsyncIterator[RabbitMqReceivedMessage]:
        with _translate_broker_errors():
            async for raw in self._iterator:
                yield _to_received(raw)

    async def complete_message(self, message: RabbitMqReceivedMessage) -> None:
        with _translate_broker_errors():
            await message.raw.ack()

    async def abandon_message(self, message: RabbitMqReceivedMessage) -> None:
        # reject (not nack) so the quorum queue counts the attempt toward x-delivery-limit.
        with _translate_broker_errors():
            await message.raw.reject(requeue=True)

    async def dead_letter_message(
        self,
        message: RabbitMqReceivedMessage,
        reason: str | None = None,
        error_description: str | None = None,
    ) -> None:
        dead_letter_queue = self._queue_name + DEAD_LETTER_QUEUE_SUFFIX
        with _translate_broker_errors():
            await self._channel.default_exchange.publish(
                _dead_letter_copy(message, reason, error_description),
                routing_key=dead_letter_queue,
                mandatory=True,
            )
            await message.raw.ack()


class RabbitMqServiceBusClient:
    def __init__(self, url: str) -> None:
        if not url:
            raise ValueError("rabbitmq_url is not configured.")
        self._url = url
        self._connection: AbstractRobustConnection | None = None
        self._publish_channel: AbstractChannel | None = None
        self._lock = asyncio.Lock()

    def get_topic_sender(self, topic_name: str) -> _RabbitMqTopicSender:
        return _RabbitMqTopicSender(self, topic_name)

    def get_subscription_receiver(
        self, topic_name: str, subscription_name: str, **_session_options: Any
    ) -> _RabbitMqSubscriptionReceiver:
        return _RabbitMqSubscriptionReceiver(
            self, subscription_queue_name(topic_name, subscription_name)
        )

    async def publish(self, topic: str, messages: Iterable[Any]) -> None:
        with _translate_broker_errors():
            channel = await self._get_publish_channel()
            exchange = await channel.get_exchange(topic, ensure=True)
            for message in messages:
                await exchange.publish(_to_amqp_message(message), routing_key=topic)

    async def open_consumer_channel(self) -> AbstractChannel:
        connection = await self._get_connection()
        channel = await connection.channel()
        await channel.set_qos(prefetch_count=_PREFETCH_COUNT)
        return channel

    async def close(self) -> None:
        if self._connection is not None and not self._connection.is_closed:
            await self._connection.close()

    async def _get_connection(self) -> AbstractRobustConnection:
        async with self._lock:
            if self._connection is None:
                self._connection = await aio_pika.connect_robust(self._url)
            return self._connection

    async def _get_publish_channel(self) -> AbstractChannel:
        connection = await self._get_connection()
        async with self._lock:
            if self._publish_channel is None or self._publish_channel.is_closed:
                self._publish_channel = await connection.channel()
            return self._publish_channel
