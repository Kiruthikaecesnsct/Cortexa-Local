import asyncio
import json
from unittest.mock import AsyncMock, MagicMock, patch

from extraction.application.handlers.process_extraction_request_handler import ProcessOutcome
from extraction.infrastructure.servicebus.servicebus_consumer import (
    ServiceBusConsumer,
    ServiceBusConsumerConfig,
)

BATCH_ID = "batch-001"
DOCUMENT_ID = "doc-001"
CORRELATION_ID = "corr-001"
MAX_ATTEMPTS = 5


def _make_message(body: bytes | str, delivery_count: int = 0) -> MagicMock:
    msg = MagicMock()
    msg.body = body if isinstance(body, bytes) else body.encode("utf-8")
    msg.delivery_count = delivery_count
    return msg


def _make_valid_body() -> bytes:
    payload = {
        "schema_version": "1.0",
        "event_version": "1.0",
        "event_type": "extraction.requested",
        "batch_id": BATCH_ID,
        "document_id": DOCUMENT_ID,
        "correlation_id": CORRELATION_ID,
        "occurred_at": "2026-01-01T00:00:00Z",
        "payload": {},
    }
    return json.dumps(payload).encode("utf-8")


def _make_consumer(
    handler_outcome: ProcessOutcome = ProcessOutcome.SUCCESS, gate=None
) -> ServiceBusConsumer:
    handler = MagicMock(handle=AsyncMock(return_value=handler_outcome))
    client = MagicMock()
    config = ServiceBusConsumerConfig(
        topic="extraction.requested",
        subscription="extraction",
        max_attempts=MAX_ATTEMPTS,
        session_idle_timeout=5.0,
        session_lock_renewal_seconds=1800.0,
    )
    return ServiceBusConsumer(
        client=client,
        handler=handler,
        config=config,
        gate=gate,
    )


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
    consumer = _make_consumer(ProcessOutcome.SUCCESS)
    message = _make_message(_make_valid_body(), delivery_count=0)
    receiver = await _run_process_one(consumer, message)

    receiver.complete_message.assert_awaited_once_with(message)
    receiver.dead_letter_message.assert_not_awaited()
    receiver.abandon_message.assert_not_awaited()


async def test_permanent_outcome_calls_dead_letter():
    consumer = _make_consumer(ProcessOutcome.PERMANENT)
    message = _make_message(_make_valid_body(), delivery_count=0)
    receiver = await _run_process_one(consumer, message)

    receiver.dead_letter_message.assert_awaited_once()
    call_kwargs = receiver.dead_letter_message.call_args
    assert call_kwargs.kwargs.get("reason") == "PermanentError"
    receiver.complete_message.assert_not_awaited()
    receiver.abandon_message.assert_not_awaited()


async def test_transient_below_max_attempts_calls_abandon():
    consumer = _make_consumer(ProcessOutcome.TRANSIENT)
    message = _make_message(_make_valid_body(), delivery_count=0)
    receiver = await _run_process_one(consumer, message)

    receiver.abandon_message.assert_awaited_once_with(message)
    receiver.complete_message.assert_not_awaited()
    receiver.dead_letter_message.assert_not_awaited()


async def test_transient_at_max_attempts_calls_dead_letter():
    consumer = _make_consumer(ProcessOutcome.TRANSIENT)
    message = _make_message(_make_valid_body(), delivery_count=MAX_ATTEMPTS - 1)
    receiver = await _run_process_one(consumer, message)

    receiver.dead_letter_message.assert_awaited_once()
    call_kwargs = receiver.dead_letter_message.call_args
    assert call_kwargs.kwargs.get("reason") == "RetriesExhausted"
    receiver.complete_message.assert_not_awaited()
    receiver.abandon_message.assert_not_awaited()


async def test_malformed_json_body_calls_dead_letter():
    consumer = _make_consumer(ProcessOutcome.SUCCESS)
    message = _make_message(b"not valid json", delivery_count=0)
    receiver = await _run_process_one(consumer, message)

    receiver.dead_letter_message.assert_awaited_once()
    call_kwargs = receiver.dead_letter_message.call_args
    assert call_kwargs.kwargs.get("reason") == "MalformedEnvelope"
    receiver.complete_message.assert_not_awaited()
    receiver.abandon_message.assert_not_awaited()


async def test_cancellation_event_stops_run_loop():
    consumer = _make_consumer(ProcessOutcome.SUCCESS)
    cancel_event = asyncio.Event()
    cancel_event.set()

    with patch.object(consumer, "_accept_session", new_callable=AsyncMock) as mock_accept:
        await consumer.run(cancel_event)
        mock_accept.assert_not_awaited()


async def test_operation_timeout_does_not_crash_run_loop():
    from azure.servicebus.exceptions import OperationTimeoutError

    consumer = _make_consumer(ProcessOutcome.SUCCESS)
    cancel_event = asyncio.Event()
    call_count = 0

    async def fake_accept(cancellation):
        nonlocal call_count
        call_count += 1
        if call_count == 1:
            raise OperationTimeoutError()
        cancellation.set()

    with patch.object(consumer, "_accept_session", side_effect=fake_accept):
        await consumer.run(cancel_event)

    assert call_count == 2


async def test_string_body_is_decoded_correctly():
    consumer = _make_consumer(ProcessOutcome.SUCCESS)
    body_str = _make_valid_body().decode("utf-8")
    message = _make_message(body_str, delivery_count=0)
    receiver = await _run_process_one(consumer, message)

    receiver.complete_message.assert_awaited_once_with(message)


async def test_terminal_batch_skips_handler_and_completes():
    gate = MagicMock(is_terminal=AsyncMock(return_value=True))
    handler = MagicMock(handle=AsyncMock(return_value=ProcessOutcome.SUCCESS))
    consumer = _make_consumer(ProcessOutcome.SUCCESS, gate=gate)
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
    consumer = _make_consumer(ProcessOutcome.SUCCESS, gate=gate)
    consumer._handler = handler

    message = _make_message(_make_valid_body(), delivery_count=0)
    receiver = await _run_process_one(consumer, message)

    gate.is_terminal.assert_awaited_once_with(BATCH_ID)
    handler.handle.assert_awaited_once()
    receiver.complete_message.assert_awaited_once_with(message)


async def test_no_gate_runs_normal_path():
    handler = MagicMock(handle=AsyncMock(return_value=ProcessOutcome.SUCCESS))
    consumer = _make_consumer(ProcessOutcome.SUCCESS, gate=None)
    consumer._handler = handler

    message = _make_message(_make_valid_body(), delivery_count=0)
    receiver = await _run_process_one(consumer, message)

    handler.handle.assert_awaited_once()
    receiver.complete_message.assert_awaited_once_with(message)
