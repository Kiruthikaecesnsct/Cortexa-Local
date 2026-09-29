import logging
import time

import httpx
import pytest
import respx

from evidence.domain.errors.evidence_errors import PatentApiError
from evidence.infrastructure.patent_apis.epo_adapter import EpoAdapter
from tests.conftest import EPO_BASE, EPO_TOKEN_RESPONSE

_EMPTY_SEARCH_JSON = {
    "ops:world-patent-data": {
        "ops:biblio-search": {"ops:search-result": {"ops:publication-reference": []}}
    }
}

_SEARCH_PATH = "/3.2/rest-services/published-data/search"
_TOKEN_PATH = "/3.2/auth/accesstoken"


def _fast_settings(evidence_settings, **overrides):
    update = {
        "epo_search_max_rps_per_replica": 100.0,
        "epo_throttle_retry_budget_seconds": 1.0,
        "epo_throttle_max_retries": 3,
    }
    update.update(overrides)
    return evidence_settings.model_copy(update=update)


async def test_403_throttle_indicator_retries_then_succeeds(
    evidence_settings, fake_secrets, monkeypatch, caplog
):
    monkeypatch.setattr(
        "evidence.infrastructure.patent_apis.epo_adapter.asyncio.sleep", _noop_sleep
    )
    settings = _fast_settings(evidence_settings)
    call_count = {"search": 0}

    def _search_handler(_request: httpx.Request) -> httpx.Response:
        call_count["search"] += 1
        if call_count["search"] == 1:
            return httpx.Response(
                403,
                headers={"x-throttling-control": "search=red:0", "Retry-After": "0"},
                text="Forbidden",
            )
        return httpx.Response(200, json=_EMPTY_SEARCH_JSON)

    with respx.mock(base_url=EPO_BASE, assert_all_called=False) as mock:
        mock.post(_TOKEN_PATH).mock(return_value=httpx.Response(200, json=EPO_TOKEN_RESPONSE))
        search_route = mock.get(_SEARCH_PATH).mock(side_effect=_search_handler)

        async with httpx.AsyncClient() as client:
            adapter = EpoAdapter(settings, fake_secrets, client)
            with caplog.at_level(logging.INFO):
                results = await adapter.search("sparse tensor", limit=5)

    assert results == []
    assert search_route.call_count == 2
    throttle_logs = [r for r in caplog.records if r.getMessage() == "epo_throttle"]
    assert len(throttle_logs) == 1
    assert throttle_logs[0].throttle_kind == "403-throttle"
    assert throttle_logs[0].status_code == 403


async def test_403_forbidden_no_throttle_indicator_fails_fast(evidence_settings, fake_secrets):
    settings = _fast_settings(evidence_settings)

    with respx.mock(base_url=EPO_BASE, assert_all_called=False) as mock:
        mock.post(_TOKEN_PATH).mock(return_value=httpx.Response(200, json=EPO_TOKEN_RESPONSE))
        search_route = mock.get(_SEARCH_PATH).mock(
            return_value=httpx.Response(403, text="Forbidden")
        )

        async with httpx.AsyncClient() as client:
            adapter = EpoAdapter(settings, fake_secrets, client)
            start = time.monotonic()
            with pytest.raises(PatentApiError) as exc_info:
                await adapter.search("sparse tensor", limit=5)
            elapsed = time.monotonic() - start

    assert exc_info.value.status_code == 403
    assert search_route.call_count == 1
    assert elapsed < 0.5


async def _noop_sleep(_delay: float) -> None:
    return None


async def test_retry_after_header_honored_and_capped_by_budget(
    evidence_settings, fake_secrets, monkeypatch
):
    sleeps: list[float] = []

    async def _recording_sleep(delay: float) -> None:
        sleeps.append(delay)

    monkeypatch.setattr(
        "evidence.infrastructure.patent_apis.epo_adapter.asyncio.sleep", _recording_sleep
    )
    settings = _fast_settings(evidence_settings, epo_throttle_retry_budget_seconds=0.5)
    call_count = {"search": 0}

    def _search_handler(_request: httpx.Request) -> httpx.Response:
        call_count["search"] += 1
        if call_count["search"] == 1:
            return httpx.Response(
                403,
                headers={"x-throttling-control": "search=yellow:1", "Retry-After": "50"},
                text="Forbidden",
            )
        return httpx.Response(200, json=_EMPTY_SEARCH_JSON)

    with respx.mock(base_url=EPO_BASE, assert_all_called=False) as mock:
        mock.post(_TOKEN_PATH).mock(return_value=httpx.Response(200, json=EPO_TOKEN_RESPONSE))
        mock.get(_SEARCH_PATH).mock(side_effect=_search_handler)

        async with httpx.AsyncClient() as client:
            adapter = EpoAdapter(settings, fake_secrets, client)
            results = await adapter.search("sparse tensor", limit=5)

    assert results == []
    assert len(sleeps) == 1
    assert 0 < sleeps[0] <= settings.epo_throttle_retry_budget_seconds
    assert sleeps[0] < 50


async def test_500_busy_retried_within_bounded_budget(evidence_settings, fake_secrets, monkeypatch):
    monkeypatch.setattr(
        "evidence.infrastructure.patent_apis.epo_adapter.asyncio.sleep", _noop_sleep
    )
    settings = _fast_settings(evidence_settings)
    call_count = {"search": 0}

    def _search_handler(_request: httpx.Request) -> httpx.Response:
        call_count["search"] += 1
        if call_count["search"] < 2:
            return httpx.Response(500, text="Server Error")
        return httpx.Response(200, json=_EMPTY_SEARCH_JSON)

    with respx.mock(base_url=EPO_BASE, assert_all_called=False) as mock:
        mock.post(_TOKEN_PATH).mock(return_value=httpx.Response(200, json=EPO_TOKEN_RESPONSE))
        search_route = mock.get(_SEARCH_PATH).mock(side_effect=_search_handler)

        async with httpx.AsyncClient() as client:
            adapter = EpoAdapter(settings, fake_secrets, client)
            results = await adapter.search("sparse tensor", limit=5)

    assert results == []
    assert search_route.call_count == 2


async def test_total_epo_retry_time_never_exceeds_budget(evidence_settings, fake_secrets):
    settings = _fast_settings(
        evidence_settings,
        epo_throttle_retry_budget_seconds=0.15,
        epo_throttle_max_retries=5,
    )

    with respx.mock(base_url=EPO_BASE, assert_all_called=False) as mock:
        mock.post(_TOKEN_PATH).mock(return_value=httpx.Response(200, json=EPO_TOKEN_RESPONSE))
        search_route = mock.get(_SEARCH_PATH).mock(
            return_value=httpx.Response(500, text="Server Error")
        )

        async with httpx.AsyncClient() as client:
            adapter = EpoAdapter(settings, fake_secrets, client)
            start = time.monotonic()
            with pytest.raises(PatentApiError):
                await adapter.search("sparse tensor", limit=5)
            elapsed = time.monotonic() - start

    assert elapsed < 1.0
    assert search_route.call_count < settings.epo_throttle_max_retries
