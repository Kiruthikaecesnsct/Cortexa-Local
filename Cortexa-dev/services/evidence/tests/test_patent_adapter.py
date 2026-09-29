import asyncio
import time

import httpx
import pytest

from evidence.infrastructure.patent_apis.patent_adapter import (
    RateLimiter,
    ThrottleInfo,
    is_epo_throttle_403,
    parse_throttling_control,
)


def _response(status_code: int, headers: dict[str, str] | None = None) -> httpx.Response:
    request = httpx.Request("GET", "http://test-epo/search")
    return httpx.Response(status_code, headers=headers or {}, request=request)


def test_parse_throttling_control_green_search() -> None:
    info = parse_throttling_control("search=green:5,inpadoc=green:2")
    assert info == ThrottleInfo(
        search_colour="green", search_limit=5, raw="search=green:5,inpadoc=green:2"
    )


def test_parse_throttling_control_yellow_search() -> None:
    info = parse_throttling_control("search=yellow:1")
    assert info is not None
    assert info.search_colour == "yellow"
    assert info.search_limit == 1


def test_parse_throttling_control_red_search() -> None:
    info = parse_throttling_control("search=red:0")
    assert info is not None
    assert info.search_colour == "red"
    assert info.search_limit == 0


def test_parse_throttling_control_missing_search_service() -> None:
    info = parse_throttling_control("images=green:5,retrieval=green:5")
    assert info is not None
    assert info.search_colour is None
    assert info.search_limit is None


def test_parse_throttling_control_none_header() -> None:
    assert parse_throttling_control(None) is None


def test_parse_throttling_control_empty_header() -> None:
    assert parse_throttling_control("") is None


def test_parse_throttling_control_unparseable_but_overloaded_keyword() -> None:
    info = parse_throttling_control("system is overloaded, please retry")
    assert info is not None
    assert info.search_colour is None


def test_parse_throttling_control_completely_unrelated_header() -> None:
    assert parse_throttling_control("no-cache, no-store") is None


def test_is_epo_throttle_403_true_when_search_red() -> None:
    resp = _response(403, {"x-throttling-control": "search=red:0"})
    assert is_epo_throttle_403(resp) is True


def test_is_epo_throttle_403_true_when_search_yellow() -> None:
    resp = _response(403, {"x-throttling-control": "search=yellow:1"})
    assert is_epo_throttle_403(resp) is True


def test_is_epo_throttle_403_false_when_search_green() -> None:
    resp = _response(403, {"x-throttling-control": "search=green:5"})
    assert is_epo_throttle_403(resp) is False


def test_is_epo_throttle_403_false_when_no_header() -> None:
    resp = _response(403, {})
    assert is_epo_throttle_403(resp) is False


def test_is_epo_throttle_403_false_when_not_403() -> None:
    resp = _response(500, {"x-throttling-control": "search=red:0"})
    assert is_epo_throttle_403(resp) is False


async def test_rate_limiter_spaces_out_burst_at_configured_rate() -> None:
    limiter = RateLimiter(rate_per_second=10.0, burst_capacity=1.0)
    await limiter.acquire()  # consumes the initial token instantly

    start = time.monotonic()
    for _ in range(3):
        await limiter.acquire()
    elapsed = time.monotonic() - start

    # 3 more acquisitions at 10/s (~0.1s apart) should take roughly 0.3s,
    # comfortably under 1s and well above near-zero (proving spacing happened).
    assert elapsed >= 0.2
    assert elapsed < 1.0


async def test_rate_limiter_allows_immediate_burst_up_to_capacity() -> None:
    limiter = RateLimiter(rate_per_second=10.0, burst_capacity=3.0)

    start = time.monotonic()
    for _ in range(3):
        await limiter.acquire()
    elapsed = time.monotonic() - start

    assert elapsed < 0.05


async def test_rate_limiter_refills_over_time() -> None:
    limiter = RateLimiter(rate_per_second=20.0, burst_capacity=1.0)
    await limiter.acquire()
    await asyncio.sleep(0.1)

    start = time.monotonic()
    await limiter.acquire()
    elapsed = time.monotonic() - start

    assert elapsed < 0.05


@pytest.mark.parametrize("rate", [0.5, 1.0])
def test_rate_limiter_capacity_defaults_to_rate_when_at_least_one(rate: float) -> None:
    limiter = RateLimiter(rate_per_second=rate)
    assert limiter._capacity == max(rate, 1.0)
