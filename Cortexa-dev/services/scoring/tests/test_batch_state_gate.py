import time
from unittest.mock import AsyncMock, MagicMock

from azure.cosmos.exceptions import CosmosResourceNotFoundError

from scoring.infrastructure.cosmos.batch_state_gate import TerminalBatchGate

BATCH_ID = "batch-123"
CACHE_TTL = 0.1


def _make_container(state: str | None = "InProgress") -> MagicMock:
    container = MagicMock()
    if state is None:
        container.read_item = AsyncMock(side_effect=CosmosResourceNotFoundError())
    else:
        container.read_item = AsyncMock(return_value={"id": BATCH_ID, "state": state})
    return container


async def test_terminal_state_returns_true():
    for state in ["Failed", "Cancelled", "Completed"]:
        container = _make_container(state)
        gate = TerminalBatchGate(container, CACHE_TTL)

        result = await gate.is_terminal(BATCH_ID)

        assert result is True
        container.read_item.assert_awaited_once_with(item=BATCH_ID, partition_key=BATCH_ID)


async def test_non_terminal_state_returns_false():
    for state in ["Queued", "InProgress"]:
        container = _make_container(state)
        gate = TerminalBatchGate(container, CACHE_TTL)

        result = await gate.is_terminal(BATCH_ID)

        assert result is False
        container.read_item.assert_awaited_once()


async def test_not_found_returns_true():
    container = _make_container(None)
    gate = TerminalBatchGate(container, CACHE_TTL)

    result = await gate.is_terminal(BATCH_ID)

    assert result is True
    container.read_item.assert_awaited_once()


async def test_terminal_state_cached_forever():
    container = _make_container("Completed")
    gate = TerminalBatchGate(container, CACHE_TTL)

    await gate.is_terminal(BATCH_ID)
    time.sleep(CACHE_TTL + 0.05)
    result = await gate.is_terminal(BATCH_ID)

    assert result is True
    container.read_item.assert_awaited_once()


async def test_non_terminal_state_cache_expires():
    container = _make_container("InProgress")
    gate = TerminalBatchGate(container, CACHE_TTL)

    await gate.is_terminal(BATCH_ID)
    time.sleep(CACHE_TTL + 0.05)
    await gate.is_terminal(BATCH_ID)

    assert container.read_item.await_count == 2


async def test_non_terminal_state_cached_within_ttl():
    container = _make_container("InProgress")
    gate = TerminalBatchGate(container, CACHE_TTL)

    await gate.is_terminal(BATCH_ID)
    result = await gate.is_terminal(BATCH_ID)

    assert result is False
    container.read_item.assert_awaited_once()


async def test_different_batches_cached_independently():
    container = _make_container("InProgress")
    gate = TerminalBatchGate(container, CACHE_TTL)

    await gate.is_terminal("batch-1")
    await gate.is_terminal("batch-2")

    assert container.read_item.await_count == 2


async def test_read_error_propagates():
    container = MagicMock()
    container.read_item = AsyncMock(side_effect=RuntimeError("cosmos down"))
    gate = TerminalBatchGate(container, CACHE_TTL)

    try:
        await gate.is_terminal(BATCH_ID)
        assert False, "Expected RuntimeError to propagate"
    except RuntimeError as exc:
        assert "cosmos down" in str(exc)
