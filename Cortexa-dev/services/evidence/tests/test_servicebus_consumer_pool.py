import asyncio
import json
from contextlib import contextmanager
from unittest.mock import AsyncMock, MagicMock, patch

import pytest

from evidence.application.handlers.process_evidence_request_handler import ProcessOutcome
from evidence.infrastructure.servicebus.servicebus_consumer import ServiceBusConsumer

MAX_ATTEMPTS = 5
POOL_SIZE = 3
BARRIER_TIMEOUT_SECONDS = 1.0
DRAIN_TIMEOUT_SECONDS = 1.0
IDLE_SETTLE_TIMEOUT_SECONDS = 1.0
MIXED_OUTCOMES = [ProcessOutcome.SUCCESS, ProcessOutcome.PERMANENT, ProcessOutcome.TRANSIENT]
RENEWER_PATCH_TARGET = "evidence.infrastructure.servicebus.servicebus_consumer.AutoLockRenewer"


class _ConcurrencyBarrier:
    def __init__(self, expected: int) -> None:
        self._expected = expected
        self.entered = 0
        self.all_entered = asyncio.Event()
        self.release = asyncio.Event()

    async def wait_and_release(self) -> None:
        self.entered += 1
        if self.entered >= self._expected:
            self.all_entered.set()
        await self.release.wait()


def _make_message(session_index: int, delivery_count: int = 0) -> MagicMock:
    payload = {
        "event_type": "evidence.requested",
        "batch_id": f"batch-pool-{session_index}",
        "document_id": f"doc-pool-{session_index}",
        "correlation_id": f"corr-pool-{session_index}",
        "occurred_at": "2026-01-01T00:00:00Z",
        "payload": {
            "candidate_id": f"cand-pool-{session_index}",
            "candidate_text": "A novel compression method.",
        },
    }
    message = MagicMock()
    message.body = json.dumps(payload).encode("utf-8")
    message.delivery_count = delivery_count
    return message


def _make_base_receiver() -> MagicMock:
    receiver = MagicMock()
    receiver.complete_message = AsyncMock()
    receiver.dead_letter_message = AsyncMock()
    receiver.abandon_message = AsyncMock()
    receiver.session = MagicMock()
    receiver.__aenter__ = AsyncMock(return_value=receiver)
    receiver.__aexit__ = AsyncMock(return_value=None)
    return receiver


def _make_session_receiver(message: MagicMock) -> MagicMock:
    receiver = _make_base_receiver()

    async def _yield_once():
        yield message

    receiver.__aiter__ = MagicMock(side_effect=lambda: _yield_once())
    return receiver


def _make_idle_receiver() -> MagicMock:
    receiver = _make_base_receiver()
    cancelled_flag = {"value": False}

    async def _hang_forever():
        try:
            await asyncio.Event().wait()
            yield
        except asyncio.CancelledError:
            cancelled_flag["value"] = True
            raise

    receiver.__aiter__ = MagicMock(side_effect=lambda: _hang_forever())
    receiver.cancelled_flag = cancelled_flag
    return receiver


def _make_raising_receiver(exc: BaseException) -> MagicMock:
    receiver = _make_base_receiver()

    async def _raise_immediately():
        raise exc
        yield

    receiver.__aiter__ = MagicMock(side_effect=lambda: _raise_immediately())
    return receiver


def _make_pool_consumer(handler_handle: AsyncMock) -> ServiceBusConsumer:
    handler = MagicMock(handle=handler_handle)
    client = MagicMock()
    return ServiceBusConsumer(
        client=client,
        handler=handler,
        topic="evidence.requested",
        subscription="evidence",
        max_attempts=MAX_ATTEMPTS,
        max_concurrent_sessions=POOL_SIZE,
    )


def _install_receiver_sequence(consumer: ServiceBusConsumer, receivers: list) -> None:
    remaining = list(receivers)

    def _get_receiver(**_kwargs):
        if remaining:
            return remaining.pop(0)
        return _make_idle_receiver()

    consumer._client.get_subscription_receiver = MagicMock(side_effect=_get_receiver)


@contextmanager
def _patched_auto_lock_renewer():
    with patch(RENEWER_PATCH_TARGET) as mock_renewer_cls:
        renewer = MagicMock()
        renewer.register = MagicMock()
        mock_renewer_cls.return_value.__aenter__ = AsyncMock(return_value=renewer)
        mock_renewer_cls.return_value.__aexit__ = AsyncMock(return_value=None)
        yield renewer


async def _run_pool_to_barrier_release(
    consumer: ServiceBusConsumer, barrier: _ConcurrencyBarrier, cancel_event: asyncio.Event
) -> asyncio.Task:
    task = asyncio.create_task(consumer.run(cancel_event))
    await asyncio.wait_for(barrier.all_entered.wait(), BARRIER_TIMEOUT_SECONDS)
    cancel_event.set()
    barrier.release.set()
    await asyncio.wait_for(task, DRAIN_TIMEOUT_SECONDS)
    return task


async def test_run_processes_pool_sessions_concurrently():
    barrier = _ConcurrencyBarrier(expected=POOL_SIZE)

    async def _handle(_envelope):
        await barrier.wait_and_release()
        return ProcessOutcome.SUCCESS

    consumer = _make_pool_consumer(AsyncMock(side_effect=_handle))
    messages = [_make_message(index) for index in range(POOL_SIZE)]
    receivers = [_make_session_receiver(message) for message in messages]
    _install_receiver_sequence(consumer, receivers)
    cancel_event = asyncio.Event()

    with _patched_auto_lock_renewer():
        await _run_pool_to_barrier_release(consumer, barrier, cancel_event)

    assert barrier.entered == POOL_SIZE
    assert consumer._client.get_subscription_receiver.call_count == POOL_SIZE
    for receiver, message in zip(receivers, messages):
        receiver.complete_message.assert_awaited_once_with(message)


async def test_run_settles_each_pool_message_exactly_once():
    barrier = _ConcurrencyBarrier(expected=POOL_SIZE)
    outcome_by_batch_suffix = dict(enumerate(MIXED_OUTCOMES))

    async def _handle(envelope):
        await barrier.wait_and_release()
        session_index = int(envelope.batch_id.rsplit("-", 1)[-1])
        return outcome_by_batch_suffix[session_index]

    consumer = _make_pool_consumer(AsyncMock(side_effect=_handle))
    messages = [_make_message(index) for index in range(POOL_SIZE)]
    receivers = [_make_session_receiver(message) for message in messages]
    _install_receiver_sequence(consumer, receivers)
    cancel_event = asyncio.Event()

    with _patched_auto_lock_renewer():
        await _run_pool_to_barrier_release(consumer, barrier, cancel_event)

    for receiver in receivers:
        settle_call_count = (
            receiver.complete_message.await_count
            + receiver.dead_letter_message.await_count
            + receiver.abandon_message.await_count
        )
        assert settle_call_count == 1


async def test_run_cancellation_drains_all_worker_tasks():
    consumer = _make_pool_consumer(AsyncMock(return_value=ProcessOutcome.SUCCESS))
    consumer._client.get_subscription_receiver = MagicMock(
        side_effect=lambda **_kwargs: _make_idle_receiver()
    )
    captured_workers: list[asyncio.Task] = []
    original_spawn_workers = consumer._spawn_workers

    def _spy_spawn_workers(cancellation: asyncio.Event) -> list[asyncio.Task]:
        workers = original_spawn_workers(cancellation)
        captured_workers.extend(workers)
        return workers

    consumer._spawn_workers = _spy_spawn_workers
    cancel_event = asyncio.Event()

    with _patched_auto_lock_renewer():
        task = asyncio.create_task(consumer.run(cancel_event))
        await asyncio.wait_for(
            _wait_until(lambda: consumer._client.get_subscription_receiver.call_count >= POOL_SIZE),
            IDLE_SETTLE_TIMEOUT_SECONDS,
        )
        cancel_event.set()
        task.cancel()

        with pytest.raises(asyncio.CancelledError):
            await asyncio.wait_for(task, DRAIN_TIMEOUT_SECONDS)

    assert len(captured_workers) == POOL_SIZE
    assert all(worker.done() for worker in captured_workers)


async def test_run_worker_exception_propagates_and_cancels_peer_sessions():
    consumer = _make_pool_consumer(AsyncMock(return_value=ProcessOutcome.SUCCESS))
    raising_receiver = _make_raising_receiver(RuntimeError("unexpected worker failure"))
    idle_receivers = [_make_idle_receiver() for _ in range(POOL_SIZE - 1)]
    _install_receiver_sequence(consumer, [raising_receiver, *idle_receivers])
    cancel_event = asyncio.Event()

    with _patched_auto_lock_renewer():
        with pytest.raises(RuntimeError, match="unexpected worker failure"):
            await consumer.run(cancel_event)

    for idle_receiver in idle_receivers:
        assert idle_receiver.cancelled_flag["value"] is True
        idle_receiver.__aexit__.assert_awaited()


async def _wait_until(predicate, poll_interval_seconds: float = 0.01) -> None:
    while not predicate():
        await asyncio.sleep(poll_interval_seconds)
