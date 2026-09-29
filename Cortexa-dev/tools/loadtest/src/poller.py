import asyncio
from collections.abc import Awaitable, Callable
from dataclasses import dataclass

_TERMINAL_STATES = frozenset({"Completed", "Failed", "PartiallyFailed"})
_MAX_BACKOFF_S = 60.0
_BACKOFF_FACTOR = 1.5


@dataclass
class PollConfig:
    timeout: float
    interval: float


async def poll_until[T](
    fetch: Callable[[], Awaitable[T]],
    predicate: Callable[[T], bool],
    config: PollConfig,
) -> T:
    loop = asyncio.get_running_loop()
    deadline = loop.time() + config.timeout
    wait = config.interval

    while True:
        result = await fetch()
        if predicate(result):
            return result

        remaining = deadline - loop.time()
        if remaining <= 0:
            raise TimeoutError(f"Condition not met within {config.timeout:.0f}s")

        await asyncio.sleep(min(wait, remaining))
        wait = min(wait * _BACKOFF_FACTOR, _MAX_BACKOFF_S)


def _is_terminal(status: dict) -> bool:
    return status.get("status") in _TERMINAL_STATES


async def wait_for_batch_complete(
    fetch_status: Callable[[], Awaitable[dict]],
    config: PollConfig,
) -> dict:
    return await poll_until(fetch_status, _is_terminal, config)
