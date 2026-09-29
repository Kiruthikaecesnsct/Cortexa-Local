import asyncio
import logging
from dataclasses import dataclass

import httpx
from tenacity import retry, retry_if_exception, stop_after_attempt, stop_after_delay

from scoring.domain.errors.scoring_errors import DualScoringFailedError, SingleScoringFailedError
from scoring.infrastructure.clients.dual_response import DualCompleteResponse
from scoring.infrastructure.clients.single_response import SingleCompleteResponse

logger = logging.getLogger(__name__)


@dataclass(frozen=True)
class RetryConfig:
    max_retries: int = 5
    backoff_max_seconds: float = 15.0
    honor_retry_after: bool = True
    total_retry_budget_seconds: float = 80.0


_SINGLE_ENDPOINT = "/complete"
_DUAL_ENDPOINT = "/complete/dual"
_BODY_PREVIEW_LEN = 200


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


def _parse_retry_after(retry_after: str | None) -> float | None:
    if retry_after is None:
        return None
    try:
        return float(retry_after)
    except ValueError:
        return None


def _extract_response_from_outcome(outcome):
    if outcome is None or not outcome.failed:
        return None
    exc = outcome.exception()
    if isinstance(exc, httpx.HTTPStatusError):
        return exc.response
    return None


def _extract_retry_after_delay(response, cap: float) -> float | None:
    if response is None or response.status_code not in {429, 503, 504}:
        return None
    retry_after_header = response.headers.get("Retry-After")
    parsed_delay = _parse_retry_after(retry_after_header)
    if parsed_delay is None:
        return None
    return max(min(parsed_delay, cap), 0)


def _compute_exponential_delay(attempt: int, cap: float) -> float:
    return min(2 ** (attempt - 2), cap)


def build_retry_with_backoff_cap(
    max_retries: int,
    backoff_max: float,
    honor_retry_after: bool,
    total_budget: float,
):
    async def _smart_wait(retry_state):
        attempt = retry_state.attempt_number
        if not honor_retry_after:
            return _compute_exponential_delay(attempt, backoff_max)
        response = _extract_response_from_outcome(retry_state.outcome)
        delay_from_header = _extract_retry_after_delay(response, backoff_max)
        if delay_from_header is not None:
            return delay_from_header
        return _compute_exponential_delay(attempt, backoff_max)

    return retry(
        retry=retry_if_exception(_is_transient),
        stop=stop_after_attempt(max_retries) | stop_after_delay(total_budget),
        wait=_smart_wait,
        reraise=True,
    )


class ModelRouterClient:
    def __init__(
        self,
        client: httpx.AsyncClient,
        retry_config: RetryConfig | None = None,
    ) -> None:
        self._client = client
        config = retry_config or RetryConfig()
        self._retry_decorator = build_retry_with_backoff_cap(
            config.max_retries,
            config.backoff_max_seconds,
            config.honor_retry_after,
            config.total_retry_budget_seconds,
        )

    async def complete_single(
        self, prompt: str, model: str | None = None
    ) -> SingleCompleteResponse:
        body = {
            "task_kind": "scoring",
            "prompt": prompt,
            "evidence_refs": None,
            "options": None,
        }
        if model is not None:
            body["model"] = model

        @self._retry_decorator
        async def _attempt():
            try:
                response = await self._client.post(_SINGLE_ENDPOINT, json=body)
            except httpx.RequestError as exc:
                raise SingleScoringFailedError(str(exc)) from exc

            if not response.is_success:
                logger.debug("model-router error body: %s", response.text)
                error_message = (
                    f"model-router returned {response.status_code}: "
                    f"{response.text[:_BODY_PREVIEW_LEN]}"
                )
                raise httpx.HTTPStatusError(
                    error_message,
                    request=response.request,
                    response=response,
                )

            return SingleCompleteResponse.model_validate(response.json())

        try:
            return await _attempt()
        except httpx.HTTPStatusError as exc:
            raise SingleScoringFailedError(str(exc), status_code=exc.response.status_code) from exc

    async def complete_dual(self, prompt: str) -> DualCompleteResponse:
        body = {
            "task_kind": "scoring",
            "prompt": prompt,
            "evidence_refs": None,
            "options": None,
        }

        @self._retry_decorator
        async def _attempt():
            try:
                response = await self._client.post(_DUAL_ENDPOINT, json=body)
            except httpx.RequestError as exc:
                raise DualScoringFailedError(str(exc)) from exc

            if response.status_code == 502:
                raise DualScoringFailedError("both models failed")
            if not response.is_success:
                logger.debug("model-router error body: %s", response.text)
                error_message = (
                    f"model-router returned {response.status_code}: "
                    f"{response.text[:_BODY_PREVIEW_LEN]}"
                )
                raise httpx.HTTPStatusError(
                    error_message,
                    request=response.request,
                    response=response,
                )

            return DualCompleteResponse.model_validate(response.json())

        try:
            return await _attempt()
        except httpx.HTTPStatusError as exc:
            raise DualScoringFailedError(str(exc), status_code=exc.response.status_code) from exc
