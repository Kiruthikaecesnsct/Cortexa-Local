import asyncio
import logging
from collections.abc import Callable

from azure.servicebus import NEXT_AVAILABLE_SESSION, ServiceBusReceiveMode
from azure.servicebus.aio import AutoLockRenewer, ServiceBusClient, ServiceBusReceiver
from azure.servicebus.exceptions import OperationTimeoutError, ServiceBusError
from pydantic import ValidationError

from seeding.application.handlers.process_seeding_request_handler import (
    ProcessOutcome,
    ProcessSeedingRequestHandler,
)
from seeding.domain.events.event_envelope import EventEnvelope
from seeding.domain.events.seeding_failed import make_seeding_failed_event
from seeding.domain.ports.event_publisher_port import EventPublisherPort

logger = logging.getLogger(__name__)

_RECONNECT_DELAY_SECONDS = 5
_TRIGGER_TYPE = "pipeline"
_RETRIES_EXHAUSTED_REASON = "retries_exhausted"


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
        handler: ProcessSeedingRequestHandler,
        topic: str,
        subscription: str,
        max_attempts: int,
        publisher: EventPublisherPort,
        failed_topic: str,
        session_idle_timeout: float = 5.0,
        session_lock_renewal_seconds: float = 1800.0,
        gate=None,
        envelope_parser: Callable[[object], EventEnvelope] = _parse_envelope,
    ) -> None:
        self._client = client
        self._handler = handler
        self._topic = topic
        self._subscription = subscription
        self._max_attempts = max_attempts
        self._publisher = publisher
        self._failed_topic = failed_topic
        self._session_idle_timeout = session_idle_timeout
        self._session_lock_renewal_seconds = session_lock_renewal_seconds
        self._gate = gate
        self._envelope_parser = envelope_parser

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
            return self._envelope_parser(message)
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
        correlation_id = envelope.correlation_id

        if self._gate is not None and await self._gate.is_terminal(batch_id):
            logger.info(
                "batch_id=%s correlation_id=%s attempt=%d outcome=SKIPPED reason=batch_terminal",
                batch_id,
                correlation_id,
                attempt,
            )
            await receiver.complete_message(message)
            return

        outcome = await self._handler.handle(envelope)
        logger.info(
            "batch_id=%s correlation_id=%s attempt=%d outcome=%s",
            batch_id,
            correlation_id,
            attempt,
            outcome.value,
        )
        await self._settle(receiver, message, outcome, attempt, envelope)

    async def _settle(
        self,
        receiver: ServiceBusReceiver,
        message,
        outcome: ProcessOutcome,
        attempt: int,
        envelope: EventEnvelope,
    ) -> None:
        if outcome == ProcessOutcome.SUCCESS:
            await receiver.complete_message(message)
        elif outcome == ProcessOutcome.PERMANENT:
            await receiver.dead_letter_message(message, reason="PermanentError")
        elif attempt + 1 >= self._max_attempts:
            await self._publish_retries_exhausted(envelope)
            await receiver.dead_letter_message(message, reason="RetriesExhausted")
        else:
            await receiver.abandon_message(message)

    async def _publish_retries_exhausted(self, envelope: EventEnvelope) -> None:
        event = make_seeding_failed_event(
            batch_id=envelope.batch_id,
            document_id=envelope.document_id,
            reason=_RETRIES_EXHAUSTED_REASON,
            job_id=envelope.batch_id,
            trigger_type=_TRIGGER_TYPE,
            correlation_id=envelope.correlation_id,
        )
        await self._publisher.publish(
            self._failed_topic,
            event,
            session_id=envelope.batch_id,
            correlation_id=envelope.correlation_id,
        )
