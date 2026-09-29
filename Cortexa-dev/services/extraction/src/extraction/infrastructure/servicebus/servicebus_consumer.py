import asyncio
import logging
from dataclasses import dataclass

from azure.servicebus import NEXT_AVAILABLE_SESSION, ServiceBusReceiveMode
from azure.servicebus.aio import AutoLockRenewer, ServiceBusClient, ServiceBusReceiver
from azure.servicebus.exceptions import OperationTimeoutError, ServiceBusError
from pydantic import ValidationError

from extraction.application.handlers.process_extraction_request_handler import (
    ProcessExtractionRequestHandler,
    ProcessOutcome,
)
from extraction.domain.events.event_envelope import EventEnvelope

logger = logging.getLogger(__name__)

_RECONNECT_DELAY_SECONDS = 5


@dataclass(frozen=True)
class ServiceBusConsumerConfig:
    topic: str
    subscription: str
    max_attempts: int
    session_idle_timeout: float
    session_lock_renewal_seconds: float


def _decode_body(message) -> str:
    raw = message.body
    if isinstance(raw, (bytes, bytearray)):
        return raw.decode("utf-8")
    if isinstance(raw, str):
        return raw
    return b"".join(
        c if isinstance(c, (bytes, bytearray)) else c.encode("utf-8") for c in raw
    ).decode("utf-8")


def _parse_envelope(message) -> EventEnvelope:
    body = _decode_body(message)
    return EventEnvelope.model_validate_json(body)


class ServiceBusConsumer:
    def __init__(
        self,
        client: ServiceBusClient,
        handler: ProcessExtractionRequestHandler,
        config: ServiceBusConsumerConfig,
        gate=None,
    ) -> None:
        self._client = client
        self._handler = handler
        self._topic = config.topic
        self._subscription = config.subscription
        self._max_attempts = config.max_attempts
        self._session_idle_timeout = config.session_idle_timeout
        self._session_lock_renewal_seconds = config.session_lock_renewal_seconds
        self._gate = gate

    async def run(self, cancellation: asyncio.Event) -> None:
        while not cancellation.is_set():
            try:
                await self._accept_session(cancellation)
            except OperationTimeoutError:
                pass
            except asyncio.CancelledError:
                raise
            except ServiceBusError as exc:
                logger.error(
                    "topic=%s subscription=%s session_error=%s",
                    self._topic,
                    self._subscription,
                    exc,
                )
                await asyncio.sleep(_RECONNECT_DELAY_SECONDS)

    async def _accept_session(self, cancellation: asyncio.Event) -> None:
        async with self._client.get_subscription_receiver(
            topic_name=self._topic,
            subscription_name=self._subscription,
            session_id=NEXT_AVAILABLE_SESSION,
            receive_mode=ServiceBusReceiveMode.PEEK_LOCK,
            max_wait_time=self._session_idle_timeout,
        ) as receiver:
            async with AutoLockRenewer() as renewer:
                renewer.register(
                    receiver,
                    receiver.session,
                    max_lock_renewal_duration=self._session_lock_renewal_seconds,
                )
                async for message in receiver:
                    if cancellation.is_set():
                        await receiver.abandon_message(message)
                        break
                    await self._process_one(receiver, message)

    async def _parse_or_dead_letter(
        self, receiver: ServiceBusReceiver, message, attempt: int
    ) -> EventEnvelope | None:
        try:
            return _parse_envelope(message)
        except (ValueError, ValidationError, UnicodeDecodeError) as exc:
            logger.error(
                "attempt=%d outcome=PERMANENT reason=malformed_envelope error=%s",
                attempt,
                exc,
            )
            await receiver.dead_letter_message(
                message, reason="MalformedEnvelope", error_description=str(exc)
            )
            return None

    async def _process_one(self, receiver: ServiceBusReceiver, message) -> None:
        attempt = message.delivery_count
        envelope = await self._parse_or_dead_letter(receiver, message, attempt)
        if envelope is None:
            return

        batch_id = envelope.batch_id
        document_id = envelope.document_id or "unknown"
        correlation_id = envelope.correlation_id

        if self._gate is not None and await self._gate.is_terminal(batch_id):
            logger.info(
                "batch_id=%s document_id=%s correlation_id=%s attempt=%d "
                "outcome=SKIPPED reason=batch_terminal",
                batch_id,
                document_id,
                correlation_id,
                attempt,
            )
            await receiver.complete_message(message)
            return

        outcome = await self._handler.handle(envelope)
        logger.info(
            "batch_id=%s document_id=%s correlation_id=%s attempt=%d outcome=%s",
            batch_id,
            document_id,
            correlation_id,
            attempt,
            outcome.value,
        )
        await self._settle(receiver, message, outcome, attempt)

    async def _settle(
        self, receiver: ServiceBusReceiver, message, outcome: ProcessOutcome, attempt: int
    ) -> None:
        if outcome == ProcessOutcome.SUCCESS:
            await receiver.complete_message(message)
        elif outcome == ProcessOutcome.PERMANENT:
            await receiver.dead_letter_message(message, reason="PermanentError")
        elif attempt + 1 >= self._max_attempts:
            await receiver.dead_letter_message(message, reason="RetriesExhausted")
        else:
            await receiver.abandon_message(message)
