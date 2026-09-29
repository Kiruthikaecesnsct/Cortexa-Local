import logging
import time

from azure.cosmos.aio import ContainerProxy
from azure.cosmos.exceptions import CosmosResourceNotFoundError

logger = logging.getLogger(__name__)

_TERMINAL_STATES = frozenset({"Failed", "Cancelled", "Completed"})


class TerminalBatchGate:
    def __init__(
        self,
        container: ContainerProxy,
        cache_ttl_seconds: float,
    ) -> None:
        self._container = container
        self._cache_ttl_seconds = cache_ttl_seconds
        self._cache: dict[str, tuple[bool, float | None]] = {}

    async def is_terminal(self, batch_id: str) -> bool:
        cached = self._read_cache(batch_id)
        if cached is not None:
            return cached

        terminal = await self._fetch_batch_state(batch_id)
        self._write_cache(batch_id, terminal)
        return terminal

    def _read_cache(self, batch_id: str) -> bool | None:
        entry = self._cache.get(batch_id)
        if entry is None:
            return None

        is_terminal, expires_at = entry
        if is_terminal:
            return True

        now = time.monotonic()
        if expires_at is not None and now < expires_at:
            return False

        del self._cache[batch_id]
        return None

    def _write_cache(self, batch_id: str, is_terminal: bool) -> None:
        if is_terminal:
            self._cache[batch_id] = (True, None)
        else:
            expires_at = time.monotonic() + self._cache_ttl_seconds
            self._cache[batch_id] = (False, expires_at)

    async def _fetch_batch_state(self, batch_id: str) -> bool:
        try:
            doc = await self._container.read_item(item=batch_id, partition_key=batch_id)
            state = doc.get("state", "")
            is_terminal = state in _TERMINAL_STATES
            logger.debug("batch_id=%s state=%s is_terminal=%s", batch_id, state, is_terminal)
            return is_terminal
        except CosmosResourceNotFoundError:
            logger.info("batch_id=%s not_found treating_as_terminal", batch_id)
            return True
        except Exception as exc:
            logger.error("batch_id=%s fetch_state_error=%s", batch_id, exc)
            raise
