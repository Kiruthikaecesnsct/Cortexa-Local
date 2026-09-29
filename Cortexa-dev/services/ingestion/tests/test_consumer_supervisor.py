import asyncio
import logging
from unittest.mock import MagicMock

import pytest

from ingestion.infrastructure.observability.consumer_supervisor import (
    ConsumerHealth,
    configure_logging,
    supervise,
)


def test_consumer_health_starts_in_starting_state():
    health = ConsumerHealth()
    assert health.is_serving is True


def test_mark_alive_transitions_to_alive():
    health = ConsumerHealth()
    health.mark_alive()
    assert health.is_serving is True


def test_mark_dead_transitions_to_dead():
    health = ConsumerHealth()
    health.mark_alive()
    health.mark_dead(None)
    assert health.is_serving is False


def test_mark_dead_with_exception_stores_exception():
    health = ConsumerHealth()
    exc = ValueError("test error")
    health.mark_dead(exc)
    assert health.is_serving is False


@pytest.mark.asyncio
async def test_supervise_task_raises_marks_dead_and_logs():
    health = ConsumerHealth()
    health.mark_alive()
    logger = MagicMock(spec=logging.Logger)

    async def failing_task():
        raise RuntimeError("consumer init failed")

    task = asyncio.create_task(failing_task())
    supervise(task, health, logger)

    await asyncio.sleep(0.1)

    assert health.is_serving is False
    logger.critical.assert_called_once()
    args = logger.critical.call_args
    assert "consumer task died" in args[0][0]
    assert "exc_info" in args[1]


@pytest.mark.asyncio
async def test_supervise_task_cancelled_remains_serving():
    health = ConsumerHealth()
    health.mark_alive()
    logger = MagicMock(spec=logging.Logger)

    async def cancellable_task(event: asyncio.Event):
        await event.wait()

    cancel_event = asyncio.Event()
    task = asyncio.create_task(cancellable_task(cancel_event))
    supervise(task, health, logger)

    task.cancel()
    try:
        await task
    except asyncio.CancelledError:
        pass

    await asyncio.sleep(0.1)

    assert health.is_serving is True
    logger.critical.assert_not_called()


@pytest.mark.asyncio
async def test_supervise_task_exits_cleanly_marks_dead():
    health = ConsumerHealth()
    health.mark_alive()
    logger = MagicMock(spec=logging.Logger)

    async def exiting_task():
        return

    task = asyncio.create_task(exiting_task())
    supervise(task, health, logger)

    await asyncio.sleep(0.1)

    assert health.is_serving is False
    logger.critical.assert_called_once()
    args = logger.critical.call_args
    assert "consumer task exited unexpectedly" in args[0][0]


def test_configure_logging_silences_azure_but_keeps_app_logger_info():
    root = logging.getLogger()
    saved_handlers = root.handlers[:]
    saved_level = root.level
    root.handlers = []
    try:
        configure_logging()

        assert logging.getLogger("azure").getEffectiveLevel() == logging.WARNING
        assert logging.getLogger("ingestion.main").getEffectiveLevel() == logging.INFO
    finally:
        root.handlers = saved_handlers
        root.setLevel(saved_level)
