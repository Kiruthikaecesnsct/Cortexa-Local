import asyncio

import pytest

from evidence.application.concurrency.evidence_scheduler import EvidenceScheduler
from evidence.infrastructure.config.settings import EvidenceSettings


def _make_scheduler(
    llm_max: int = 2,
    patent_max: int = 2,
    corpus_max: int = 2,
    llm_timeout: float = 1.0,
    patent_timeout: float = 1.0,
    corpus_timeout: float = 1.0,
    deadline: float = 5.0,
) -> EvidenceScheduler:
    settings = EvidenceSettings(
        model_router_url="http://test",
        vector_router_url="http://test",
        evidence_llm_global_max_concurrency=llm_max,
        evidence_patent_global_max_concurrency=patent_max,
        evidence_corpus_global_max_concurrency=corpus_max,
        evidence_llm_acquire_timeout_seconds=llm_timeout,
        evidence_patent_acquire_timeout_seconds=patent_timeout,
        evidence_corpus_acquire_timeout_seconds=corpus_timeout,
        evidence_candidate_deadline_seconds=deadline,
    )
    return EvidenceScheduler(settings)


@pytest.mark.asyncio
async def test_acquire_llm_slot_acquires_and_releases():
    scheduler = _make_scheduler(llm_max=1)

    async with scheduler.acquire_llm_slot():
        pass


@pytest.mark.asyncio
async def test_acquire_patent_slot_acquires_and_releases():
    scheduler = _make_scheduler(patent_max=1)

    async with scheduler.acquire_patent_slot():
        pass


@pytest.mark.asyncio
async def test_acquire_corpus_slot_acquires_and_releases():
    scheduler = _make_scheduler(corpus_max=1)

    async with scheduler.acquire_corpus_slot():
        pass


@pytest.mark.asyncio
async def test_llm_slot_limits_concurrency_to_configured_max():
    scheduler = _make_scheduler(llm_max=2)
    active_count = 0
    max_observed = 0

    async def worker() -> None:
        nonlocal active_count, max_observed
        async with scheduler.acquire_llm_slot():
            active_count += 1
            max_observed = max(max_observed, active_count)
            await asyncio.sleep(0.05)
            active_count -= 1

    await asyncio.gather(*[worker() for _ in range(10)])

    assert max_observed == 2


@pytest.mark.asyncio
async def test_patent_slot_limits_concurrency_to_configured_max():
    scheduler = _make_scheduler(patent_max=3)
    active_count = 0
    max_observed = 0

    async def worker() -> None:
        nonlocal active_count, max_observed
        async with scheduler.acquire_patent_slot():
            active_count += 1
            max_observed = max(max_observed, active_count)
            await asyncio.sleep(0.05)
            active_count -= 1

    await asyncio.gather(*[worker() for _ in range(10)])

    assert max_observed == 3


@pytest.mark.asyncio
async def test_corpus_slot_limits_concurrency_to_configured_max():
    scheduler = _make_scheduler(corpus_max=4)
    active_count = 0
    max_observed = 0

    async def worker() -> None:
        nonlocal active_count, max_observed
        async with scheduler.acquire_corpus_slot():
            active_count += 1
            max_observed = max(max_observed, active_count)
            await asyncio.sleep(0.05)
            active_count -= 1

    await asyncio.gather(*[worker() for _ in range(10)])

    assert max_observed == 4


@pytest.mark.asyncio
async def test_llm_slot_acquire_timeout_raises_on_contention():
    scheduler = _make_scheduler(llm_max=1, llm_timeout=0.1)

    async def block_forever() -> None:
        async with scheduler.acquire_llm_slot():
            await asyncio.sleep(10)

    blocker_task = asyncio.create_task(block_forever())
    await asyncio.sleep(0.02)

    with pytest.raises(TimeoutError, match="LLM slot acquisition timed out"):
        async with scheduler.acquire_llm_slot():
            pass

    blocker_task.cancel()
    try:
        await blocker_task
    except asyncio.CancelledError:
        pass


@pytest.mark.asyncio
async def test_patent_slot_acquire_timeout_raises_on_contention():
    scheduler = _make_scheduler(patent_max=1, patent_timeout=0.1)

    async def block_forever() -> None:
        async with scheduler.acquire_patent_slot():
            await asyncio.sleep(10)

    blocker_task = asyncio.create_task(block_forever())
    await asyncio.sleep(0.02)

    with pytest.raises(TimeoutError, match="Patent API slot acquisition timed out"):
        async with scheduler.acquire_patent_slot():
            pass

    blocker_task.cancel()
    try:
        await blocker_task
    except asyncio.CancelledError:
        pass


@pytest.mark.asyncio
async def test_corpus_slot_acquire_timeout_raises_on_contention():
    scheduler = _make_scheduler(corpus_max=1, corpus_timeout=0.1)

    async def block_forever() -> None:
        async with scheduler.acquire_corpus_slot():
            await asyncio.sleep(10)

    blocker_task = asyncio.create_task(block_forever())
    await asyncio.sleep(0.02)

    with pytest.raises(TimeoutError, match="Corpus slot acquisition timed out"):
        async with scheduler.acquire_corpus_slot():
            pass

    blocker_task.cancel()
    try:
        await blocker_task
    except asyncio.CancelledError:
        pass


@pytest.mark.asyncio
async def test_llm_slot_released_even_on_worker_exception():
    scheduler = _make_scheduler(llm_max=1)

    with pytest.raises(ValueError, match="boom"):
        async with scheduler.acquire_llm_slot():
            raise ValueError("boom")

    async with scheduler.acquire_llm_slot():
        pass


@pytest.mark.asyncio
async def test_patent_slot_released_even_on_worker_exception():
    scheduler = _make_scheduler(patent_max=1)

    with pytest.raises(ValueError, match="boom"):
        async with scheduler.acquire_patent_slot():
            raise ValueError("boom")

    async with scheduler.acquire_patent_slot():
        pass


@pytest.mark.asyncio
async def test_corpus_slot_released_even_on_worker_exception():
    scheduler = _make_scheduler(corpus_max=1)

    with pytest.raises(ValueError, match="boom"):
        async with scheduler.acquire_corpus_slot():
            raise ValueError("boom")

    async with scheduler.acquire_corpus_slot():
        pass


@pytest.mark.asyncio
async def test_candidate_deadline_seconds_returns_configured_value():
    scheduler = _make_scheduler(deadline=120.0)

    assert scheduler.candidate_deadline_seconds() == 120.0


@pytest.mark.asyncio
async def test_different_slot_types_do_not_interfere():
    scheduler = _make_scheduler(llm_max=1, patent_max=1, corpus_max=1)

    async with scheduler.acquire_llm_slot():
        async with scheduler.acquire_patent_slot():
            async with scheduler.acquire_corpus_slot():
                pass
