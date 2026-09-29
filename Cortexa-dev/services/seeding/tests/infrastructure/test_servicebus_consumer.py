import asyncio
import json
from unittest.mock import AsyncMock, MagicMock, patch

from seeding.application.handlers.process_seeding_request_handler import ProcessOutcome
from seeding.infrastructure.servicebus.servicebus_consumer import ServiceBusConsumer

BATCH_ID = "batch-001"
DOCUMENT_ID = "doc-001"
CORRELATION_ID = "corr-001"
MAX_ATTEMPTS = 5
FAILED_TOPIC = "seeding.failed"


def _make_message(body: bytes | str, delivery_count: int = 0) -> MagicMock:
    msg = MagicMock()
    msg.body = body
    msg.delivery_count = delivery_count
    return msg


def _make_valid_body() -> bytes:
    payload = {
        "batch_id": BATCH_ID,
        "document_id": DOCUMENT_ID,
        "correlation_id": CORRELATION_ID,
        "occurred_at": "2026-01-01T00:00:00Z",
        "payload": {},
    }
    return json.dumps(payload).encode("utf-8")


def _make_consumer(
    handler_outcome: ProcessOutcome = ProcessOutcome.SUCCESS,
    publisher: AsyncMock | None = None,
    gate=None,
) -> tuple[ServiceBusConsumer, AsyncMock]:
    handler = MagicMock(handle=AsyncMock(return_value=handler_outcome))
    client = MagicMock()
    publisher = publisher or AsyncMock()
    consumer = ServiceBusConsumer(
        client=client,
        handler=handler,
        topic="seeding.requested",
        subscription="seeding",
        max_attempts=MAX_ATTEMPTS,
        publisher=publisher,
        failed_topic=FAILED_TOPIC,
        gate=gate,
    )
    return consumer, publisher


def _make_receiver(messages: list) -> MagicMock:
    receiver = MagicMock()
    receiver.complete_message = AsyncMock()
    receiver.dead_letter_message = AsyncMock()
    receiver.abandon_message = AsyncMock()
    receiver.__aenter__ = AsyncMock(return_value=receiver)
    receiver.__aexit__ = AsyncMock(return_value=None)
    receiver.__aiter__ = MagicMock(return_value=iter(messages))
    return receiver


async def _run_process_one(consumer: ServiceBusConsumer, message) -> MagicMock:
    receiver = _make_receiver([])
    await consumer._process_one(receiver, message)
    return receiver


async def test_success_outcome_calls_complete():
    consumer, publisher = _make_consumer(ProcessOutcome.SUCCESS)
    message = _make_message(_make_valid_body(), delivery_count=0)

    receiver = await _run_process_one(consumer, message)

    receiver.complete_message.assert_awaited_once_with(message)
    receiver.dead_letter_message.assert_not_awaited()
    receiver.abandon_message.assert_not_awaited()
    publisher.publish.assert_not_awaited()


async def test_permanent_outcome_calls_dead_letter_without_publish():
    consumer, publisher = _make_consumer(ProcessOutcome.PERMANENT)
    message = _make_message(_make_valid_body(), delivery_count=0)

    receiver = await _run_process_one(consumer, message)

    receiver.dead_letter_message.assert_awaited_once()
    assert receiver.dead_letter_message.call_args.kwargs.get("reason") == "PermanentError"
    receiver.complete_message.assert_not_awaited()
    receiver.abandon_message.assert_not_awaited()
    publisher.publish.assert_not_awaited()


async def test_transient_below_max_attempts_calls_abandon():
    consumer, publisher = _make_consumer(ProcessOutcome.TRANSIENT)
    message = _make_message(_make_valid_body(), delivery_count=0)

    receiver = await _run_process_one(consumer, message)

    receiver.abandon_message.assert_awaited_once_with(message)
    receiver.complete_message.assert_not_awaited()
    receiver.dead_letter_message.assert_not_awaited()
    publisher.publish.assert_not_awaited()


async def test_transient_at_max_attempts_publishes_seeding_failed_then_dead_letters():
    consumer, publisher = _make_consumer(ProcessOutcome.TRANSIENT)
    message = _make_message(_make_valid_body(), delivery_count=MAX_ATTEMPTS - 1)

    receiver = await _run_process_one(consumer, message)

    publisher.publish.assert_awaited_once()
    topic, event = publisher.publish.await_args.args
    assert topic == FAILED_TOPIC
    assert event["event_type"] == "seeding.failed"
    assert event["batch_id"] == BATCH_ID
    assert event["document_id"] == DOCUMENT_ID
    assert event["payload"]["job_id"] == BATCH_ID
    assert event["payload"]["reason"] == "retries_exhausted"
    kwargs = publisher.publish.await_args.kwargs
    assert kwargs.get("session_id") == BATCH_ID
    assert kwargs.get("correlation_id") == CORRELATION_ID

    receiver.dead_letter_message.assert_awaited_once()
    assert receiver.dead_letter_message.call_args.kwargs.get("reason") == "RetriesExhausted"
    receiver.complete_message.assert_not_awaited()
    receiver.abandon_message.assert_not_awaited()


async def test_permanent_outcome_at_max_attempts_does_not_double_publish():
    consumer, publisher = _make_consumer(ProcessOutcome.PERMANENT)
    message = _make_message(_make_valid_body(), delivery_count=MAX_ATTEMPTS - 1)

    receiver = await _run_process_one(consumer, message)

    receiver.dead_letter_message.assert_awaited_once()
    assert receiver.dead_letter_message.call_args.kwargs.get("reason") == "PermanentError"
    publisher.publish.assert_not_awaited()


async def test_malformed_json_body_calls_dead_letter_with_malformed_envelope():
    consumer, publisher = _make_consumer(ProcessOutcome.SUCCESS)
    message = _make_message(b"not valid json", delivery_count=0)

    receiver = await _run_process_one(consumer, message)

    receiver.dead_letter_message.assert_awaited_once()
    assert receiver.dead_letter_message.call_args.kwargs.get("reason") == "MalformedEnvelope"
    receiver.complete_message.assert_not_awaited()
    receiver.abandon_message.assert_not_awaited()
    consumer._handler.handle.assert_not_awaited()
    publisher.publish.assert_not_awaited()


async def test_cancellation_event_set_before_run_loop_exits_without_accepting_session():
    consumer, _publisher = _make_consumer(ProcessOutcome.SUCCESS)
    cancel_event = asyncio.Event()
    cancel_event.set()

    with patch.object(consumer, "_accept_session", new_callable=AsyncMock) as mock_accept:
        await consumer.run(cancel_event)

        mock_accept.assert_not_awaited()


async def test_string_body_is_decoded_correctly_and_calls_complete():
    consumer, _publisher = _make_consumer(ProcessOutcome.SUCCESS)
    body_str = _make_valid_body().decode("utf-8")
    message = _make_message(body_str, delivery_count=0)

    receiver = await _run_process_one(consumer, message)

    receiver.complete_message.assert_awaited_once_with(message)


async def test_accept_session_registers_auto_lock_renewer():
    consumer, _publisher = _make_consumer(ProcessOutcome.SUCCESS)
    receiver = _make_receiver([])

    async def _empty_aiter():
        return
        yield

    receiver.__aiter__ = MagicMock(return_value=_empty_aiter())
    session = MagicMock()
    receiver.session = session
    client = MagicMock()
    client.get_subscription_receiver = MagicMock(return_value=receiver)
    consumer._client = client
    cancel_event = asyncio.Event()

    with patch(
        "seeding.infrastructure.servicebus.servicebus_consumer.AutoLockRenewer"
    ) as mock_renewer_cls:
        renewer = MagicMock()
        renewer.register = MagicMock()
        mock_renewer_cls.return_value.__aenter__ = AsyncMock(return_value=renewer)
        mock_renewer_cls.return_value.__aexit__ = AsyncMock(return_value=None)

        await consumer._accept_session(cancel_event)

        renewer.register.assert_called_once_with(
            receiver,
            session,
            max_lock_renewal_duration=consumer._session_lock_renewal_seconds,
        )


async def test_terminal_batch_skips_handler_and_completes():
    gate = MagicMock(is_terminal=AsyncMock(return_value=True))
    handler = MagicMock(handle=AsyncMock(return_value=ProcessOutcome.SUCCESS))
    consumer, _publisher = _make_consumer(ProcessOutcome.SUCCESS, gate=gate)
    consumer._handler = handler

    message = _make_message(_make_valid_body(), delivery_count=0)
    receiver = await _run_process_one(consumer, message)

    gate.is_terminal.assert_awaited_once_with(BATCH_ID)
    handler.handle.assert_not_awaited()
    receiver.complete_message.assert_awaited_once_with(message)
    receiver.dead_letter_message.assert_not_awaited()
    receiver.abandon_message.assert_not_awaited()


async def test_non_terminal_batch_runs_normal_path():
    gate = MagicMock(is_terminal=AsyncMock(return_value=False))
    handler = MagicMock(handle=AsyncMock(return_value=ProcessOutcome.SUCCESS))
    consumer, _publisher = _make_consumer(ProcessOutcome.SUCCESS, gate=gate)
    consumer._handler = handler

    message = _make_message(_make_valid_body(), delivery_count=0)
    receiver = await _run_process_one(consumer, message)

    gate.is_terminal.assert_awaited_once_with(BATCH_ID)
    handler.handle.assert_awaited_once()
    receiver.complete_message.assert_awaited_once_with(message)


async def test_no_gate_runs_normal_path():
    handler = MagicMock(handle=AsyncMock(return_value=ProcessOutcome.SUCCESS))
    consumer, _publisher = _make_consumer(ProcessOutcome.SUCCESS, gate=None)
    consumer._handler = handler

    message = _make_message(_make_valid_body(), delivery_count=0)
    receiver = await _run_process_one(consumer, message)

    handler.handle.assert_awaited_once()
    receiver.complete_message.assert_awaited_once_with(message)
