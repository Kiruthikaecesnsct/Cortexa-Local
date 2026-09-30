import asyncio
import logging
from collections.abc import Coroutine
from datetime import UTC, datetime
from typing import Any

from ingestion.domain.models.repository_clone import RepositoryClone

_logger = logging.getLogger(__name__)


def utc_now() -> datetime:
    return datetime.now(UTC)


class CloneJobRegistry:
    """In-memory view of saves that have not reached storage yet.

    Jobs are keyed by the clone id derived from owner/repository/branch, so a new
    save of the same branch replaces any earlier failed attempt.
    """

    def __init__(self) -> None:
        self._jobs: dict[str, RepositoryClone] = {}

    def put(self, clone: RepositoryClone) -> None:
        self._jobs[clone.clone_id] = clone

    def update(self, clone_id: str, **changes: Any) -> RepositoryClone:
        updated = self._jobs[clone_id].model_copy(update={**changes, "updated_at": utc_now()})
        self._jobs[clone_id] = updated
        return updated

    def remove(self, clone_id: str) -> None:
        self._jobs.pop(clone_id, None)

    def all(self) -> list[RepositoryClone]:
        return list(self._jobs.values())

    def active(self, clone_id: str) -> RepositoryClone | None:
        job = self._jobs.get(clone_id)
        return job if job is not None and job.status.in_progress else None


class BackgroundRunner:
    """Keeps strong references to fire-and-forget tasks and cancels them on shutdown."""

    def __init__(self) -> None:
        self._tasks: set[asyncio.Task[None]] = set()

    def spawn(self, coro: Coroutine[Any, Any, None]) -> None:
        task = asyncio.create_task(coro)
        self._tasks.add(task)
        task.add_done_callback(self._tasks.discard)

    async def aclose(self) -> None:
        for task in self._tasks:
            task.cancel()
        results = await asyncio.gather(*self._tasks, return_exceptions=True)
        _logger.info("Cancelled %d background clone task(s)", len(results))

    @property
    def active_count(self) -> int:
        return len(self._tasks)
