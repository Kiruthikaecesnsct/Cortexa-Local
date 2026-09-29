import time
from unittest.mock import AsyncMock

import pytest
from azure.cosmos.exceptions import CosmosResourceNotFoundError

from harvesting.infrastructure.cosmos.batch_state_gate import TerminalBatchGate


@pytest.fixture
def mock_container():
    return AsyncMock()


@pytest.fixture
def gate(mock_container):
    return TerminalBatchGate(mock_container, cache_ttl_seconds=30.0)


async def test_terminal_state_returns_true(mock_container, gate):
    mock_container.read_item.return_value = {"state": "Completed", "batch_id": "b1"}
    result = await gate.is_terminal("b1")
    assert result is True


async def test_non_terminal_state_returns_false(mock_container, gate):
    mock_container.read_item.return_value = {"state": "InProgress", "batch_id": "b1"}
    result = await gate.is_terminal("b1")
    assert result is False


async def test_failed_state_is_terminal(mock_container, gate):
    mock_container.read_item.return_value = {"state": "Failed", "batch_id": "b1"}
    result = await gate.is_terminal("b1")
    assert result is True


async def test_cancelled_state_is_terminal(mock_container, gate):
    mock_container.read_item.return_value = {"state": "Cancelled", "batch_id": "b1"}
    result = await gate.is_terminal("b1")
    assert result is True


async def test_not_found_returns_true(mock_container, gate):
    mock_container.read_item.side_effect = CosmosResourceNotFoundError()
    result = await gate.is_terminal("missing-batch")
    assert result is True


async def test_terminal_result_cached_forever(mock_container, gate):
    mock_container.read_item.return_value = {"state": "Completed", "batch_id": "b1"}
    await gate.is_terminal("b1")
    await gate.is_terminal("b1")
    mock_container.read_item.assert_called_once()


async def test_non_terminal_result_expires_after_ttl(mock_container, monkeypatch):
    mock_container.read_item.return_value = {"state": "InProgress", "batch_id": "b1"}

    start = 1000.0
    current_time = start

    def mock_monotonic():
        return current_time

    monkeypatch.setattr(time, "monotonic", mock_monotonic)

    gate = TerminalBatchGate(mock_container, cache_ttl_seconds=30.0)

    result1 = await gate.is_terminal("b1")
    assert result1 is False

    result2 = await gate.is_terminal("b1")
    assert result2 is False

    current_time = start + 31.0
    result3 = await gate.is_terminal("b1")
    assert result3 is False

    assert mock_container.read_item.call_count == 2


async def test_non_terminal_result_cached_within_ttl(mock_container, monkeypatch):
    mock_container.read_item.return_value = {"state": "InProgress", "batch_id": "b1"}

    start = 1000.0
    current_time = start

    def mock_monotonic():
        return current_time

    monkeypatch.setattr(time, "monotonic", mock_monotonic)

    gate = TerminalBatchGate(mock_container, cache_ttl_seconds=30.0)

    await gate.is_terminal("b1")

    current_time = start + 10.0
    await gate.is_terminal("b1")

    mock_container.read_item.assert_called_once()


async def test_different_batches_cached_independently(mock_container, gate):
    mock_container.read_item.side_effect = [
        {"state": "Completed", "batch_id": "b1"},
        {"state": "InProgress", "batch_id": "b2"},
    ]
    result1 = await gate.is_terminal("b1")
    result2 = await gate.is_terminal("b2")
    assert result1 is True
    assert result2 is False
    assert mock_container.read_item.call_count == 2
