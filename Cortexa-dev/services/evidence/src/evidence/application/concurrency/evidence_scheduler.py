import asyncio
from collections.abc import AsyncIterator
from contextlib import asynccontextmanager

from evidence.infrastructure.config.settings import EvidenceSettings


class WeightedCapacityGate:
    """Atomic all-or-nothing acquisition of N capacity units.

    Unlike acquiring an ``asyncio.Semaphore`` N times in a loop, a caller here
    either reserves its full weight in one step or waits without holding any
    partial capacity that other callers could deadlock on.
    """

    def __init__(self, capacity: int) -> None:
        self._available = capacity
        self._condition = asyncio.Condition()

    async def acquire(self, weight: int, timeout: float) -> None:
        async def _wait_and_reserve() -> None:
            async with self._condition:
                await self._condition.wait_for(lambda: self._available >= weight)
                self._available -= weight

        try:
            await asyncio.wait_for(_wait_and_reserve(), timeout=timeout)
        except TimeoutError as exc:
            raise TimeoutError("LLM slot acquisition timed out") from exc

    async def release(self, weight: int) -> None:
        async with self._condition:
            self._available += weight
            self._condition.notify_all()


class EvidenceScheduler:
    def __init__(self, settings: EvidenceSettings) -> None:
        self._llm_gate = WeightedCapacityGate(settings.evidence_llm_global_max_concurrency)
        self._patent_semaphore = asyncio.Semaphore(settings.evidence_patent_global_max_concurrency)
        self._corpus_semaphore = asyncio.Semaphore(settings.evidence_corpus_global_max_concurrency)
        self._llm_timeout = settings.evidence_llm_acquire_timeout_seconds
        self._patent_timeout = settings.evidence_patent_acquire_timeout_seconds
        self._corpus_timeout = settings.evidence_corpus_acquire_timeout_seconds
        self._candidate_deadline = settings.evidence_candidate_deadline_seconds

    @asynccontextmanager
    async def acquire_llm_slot(self) -> AsyncIterator[None]:
        await self._llm_gate.acquire(1, self._llm_timeout)
        try:
            yield
        finally:
            await self._llm_gate.release(1)

    @asynccontextmanager
    async def acquire_patent_slot(self) -> AsyncIterator[None]:
        try:
            await asyncio.wait_for(self._patent_semaphore.acquire(), timeout=self._patent_timeout)
        except TimeoutError as exc:
            raise TimeoutError("Patent API slot acquisition timed out") from exc
        try:
            yield
        finally:
            self._patent_semaphore.release()

    @asynccontextmanager
    async def acquire_corpus_slot(self) -> AsyncIterator[None]:
        try:
            await asyncio.wait_for(self._corpus_semaphore.acquire(), timeout=self._corpus_timeout)
        except TimeoutError as exc:
            raise TimeoutError("Corpus slot acquisition timed out") from exc
        try:
            yield
        finally:
            self._corpus_semaphore.release()

    def candidate_deadline_seconds(self) -> float:
        return self._candidate_deadline
