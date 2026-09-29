import asyncio
import re
import time
from dataclasses import dataclass
from typing import Protocol

import httpx
from tenacity import (
    retry,
    retry_if_exception,
    stop_after_attempt,
    stop_after_delay,
    wait_exponential,
)

from evidence.domain.models.patent_match import PatentMatch

_THROTTLE_COLOUR_GREEN = "green"
_THROTTLE_COLOUR_YELLOW = "yellow"
_THROTTLE_COLOUR_RED = "red"
_THROTTLE_STATE_OVERLOADED = "overloaded"
_THROTTLE_SEARCH_SERVICE = "search"

_THROTTLING_CONTROL_PATTERN = re.compile(r"(\w+)=(\w+):(\d+)")


def _is_transient(exc: BaseException) -> bool:
    if isinstance(exc, asyncio.CancelledError):
        return False
    if isinstance(exc, httpx.TimeoutException):
        return True
    if isinstance(exc, httpx.NetworkError):
        return True
    if isinstance(exc, httpx.HTTPStatusError):
        return exc.response.status_code in {429, 500, 502, 503, 504}
    return False


async def retry_after_backoff(response: httpx.Response | None, minimum: float = 0.0) -> None:
    if response is None:
        return
    retry_after = response.headers.get("Retry-After")
    delay = _parse_retry_after(retry_after)
    if delay is None:
        return
    delay = max(delay, minimum)
    if delay > 0:
        await asyncio.sleep(delay)


def _parse_retry_after(retry_after: str | None) -> float | None:
    if retry_after is None:
        return None
    try:
        return float(retry_after)
    except ValueError:
        return None


def build_retry(max_retries: int):
    return retry(
        retry=retry_if_exception(_is_transient),
        stop=stop_after_attempt(max_retries),
        wait=wait_exponential(multiplier=1, min=1, max=10),
        reraise=True,
    )


def _extract_response_from_outcome(outcome):
    if outcome is None or not outcome.failed:
        return None
    exc = outcome.exception()
    if isinstance(exc, httpx.HTTPStatusError):
        return exc.response
    return None


def _extract_retry_after_delay(response, backoff_max_seconds: float) -> float | None:
    if response is None or response.status_code not in {429, 503, 504}:
        return None
    retry_after_header = response.headers.get("Retry-After")
    parsed_delay = _parse_retry_after(retry_after_header)
    if parsed_delay is None:
        return None
    return max(min(parsed_delay, backoff_max_seconds), 0)


def _compute_exponential_delay(attempt: int, backoff_max_seconds: float) -> float:
    return min(2 ** (attempt - 2), backoff_max_seconds)


def build_retry_with_backoff_cap(
    max_retries: int,
    backoff_max_seconds: float,
    honor_retry_after: bool,
    total_retry_budget_seconds: float,
):
    async def _smart_wait(retry_state):
        attempt = retry_state.attempt_number
        if attempt == 1:
            return 0
        if not honor_retry_after:
            return _compute_exponential_delay(attempt, backoff_max_seconds)
        response = _extract_response_from_outcome(retry_state.outcome)
        delay_from_header = _extract_retry_after_delay(response, backoff_max_seconds)
        if delay_from_header is not None:
            return delay_from_header
        return _compute_exponential_delay(attempt, backoff_max_seconds)

    return retry(
        retry=retry_if_exception(_is_transient),
        stop=stop_after_attempt(max_retries) | stop_after_delay(total_retry_budget_seconds),
        wait=_smart_wait,
        reraise=True,
    )


def rank_score(index: int, total: int) -> float:
    """Derive a relevance score from result rank (1.0 at rank 0, decreasing linearly).

    This is a deliberate rank-based proxy, not a similarity score: USPTO/EPO/Lens
    APIs return ordered results but expose no comparable relevance/similarity value.
    """
    return 1.0 - (index / max(total, 1))


class RateLimiter:
    def __init__(self, rate_per_second: float, burst_capacity: float | None = None) -> None:
        self._rate = rate_per_second
        self._capacity = burst_capacity if burst_capacity is not None else max(rate_per_second, 1.0)
        self._tokens = self._capacity
        self._last_refill = time.monotonic()
        self._lock = asyncio.Lock()

    def _refill(self) -> None:
        now = time.monotonic()
        elapsed = now - self._last_refill
        self._tokens = min(self._capacity, self._tokens + elapsed * self._rate)
        self._last_refill = now

    def _seconds_until_next_token(self) -> float:
        if self._tokens >= 1.0:
            return 0.0
        missing = 1.0 - self._tokens
        return missing / self._rate

    async def acquire(self) -> None:
        async with self._lock:
            while True:
                self._refill()
                if self._tokens >= 1.0:
                    self._tokens -= 1.0
                    return
                await asyncio.sleep(self._seconds_until_next_token())


@dataclass(frozen=True)
class ThrottleInfo:
    search_colour: str | None
    search_limit: int | None
    raw: str


def _parse_throttle_services(header_value: str) -> dict[str, tuple[str, int]]:
    services: dict[str, tuple[str, int]] = {}
    for name, colour, limit in _THROTTLING_CONTROL_PATTERN.findall(header_value):
        services[name.lower()] = (colour.lower(), int(limit))
    return services


def parse_throttling_control(header_value: str | None) -> ThrottleInfo | None:
    if not header_value:
        return None
    services = _parse_throttle_services(header_value)
    search_entry = services.get(_THROTTLE_SEARCH_SERVICE)
    if not services and _THROTTLE_STATE_OVERLOADED not in header_value.lower():
        return None
    if search_entry is None:
        return ThrottleInfo(search_colour=None, search_limit=None, raw=header_value)
    colour, limit = search_entry
    return ThrottleInfo(search_colour=colour, search_limit=limit, raw=header_value)


def _search_service_is_throttled(throttle_info: ThrottleInfo) -> bool:
    if throttle_info.search_colour is None:
        return _THROTTLE_STATE_OVERLOADED in throttle_info.raw.lower()
    return throttle_info.search_colour in {_THROTTLE_COLOUR_YELLOW, _THROTTLE_COLOUR_RED}


def is_epo_throttle_403(response: httpx.Response) -> bool:
    if response.status_code != 403:
        return False
    header_value = response.headers.get("x-throttling-control")
    throttle_info = parse_throttling_control(header_value)
    if throttle_info is None:
        return False
    return _search_service_is_throttled(throttle_info)


class PatentAdapter(Protocol):
    async def search(self, query: str, limit: int) -> list[PatentMatch]: ...
