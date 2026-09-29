import asyncio
import logging
from dataclasses import dataclass

import httpx
from tenacity import (
    retry,
    retry_if_exception,
    stop_after_attempt,
    stop_after_delay,
)

from seeding.domain.ports.model_router_port import ModelResult
from seeding.infrastructure.clients.complete_response import CompleteResponse

_logger = logging.getLogger(__name__)
_COMPLETE_ENDPOINT = "/complete"
_BODY_PREVIEW_LEN = 200


@dataclass(frozen=True)
class RetryConfig:
    max_retries: int
    backoff_max_seconds: float
    honor_retry_after: bool
    total_retry_budget_seconds: float


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


def _build_retry_with_backoff_cap(
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


class ModelRouterClient:
    def __init__(
        self,
        base_url: str,
        task_kind: str,
        timeout: float,
        retry_config: RetryConfig,
        max_output_tokens: int,
    ) -> None:
        self._client = httpx.AsyncClient(base_url=base_url, timeout=timeout)
        self._task_kind = task_kind
        self._max_output_tokens = max_output_tokens
        self._retry = _build_retry_with_backoff_cap(
            max_retries=retry_config.max_retries,
            backoff_max_seconds=retry_config.backoff_max_seconds,
            honor_retry_after=retry_config.honor_retry_after,
            total_retry_budget_seconds=retry_config.total_retry_budget_seconds,
        )

    async def complete(
        self, prompt: str, evidence_refs: list[str], model: str | None = None
    ) -> ModelResult:
        body = {
            "mode": "SinglePrimary",
            "task_kind": self._task_kind,
            "prompt": prompt,
            "evidence_refs": evidence_refs,
            "options": {"max_tokens": self._max_output_tokens, "force_json_output": True},
        }
        if model is not None:
            body["model"] = model
        try:
            return await self._retry(self._complete_once)(body)
        except httpx.HTTPStatusError as exc:
            from seeding.domain.errors.seeding_errors import ModelRouterFailedError

            error_message = (
                f"model-router returned {exc.response.status_code}: "
                f"{exc.response.text[:_BODY_PREVIEW_LEN]}"
            )
            raise ModelRouterFailedError(
                error_message, status_code=exc.response.status_code
            ) from exc

    async def _complete_once(self, body: dict) -> ModelResult:
        response = await self._client.post(_COMPLETE_ENDPOINT, json=body)
        response.raise_for_status()
        raw = CompleteResponse.model_validate(response.json())
        usage = raw.usage
        return ModelResult(
            content=raw.content,
            citations=raw.citations or [],
            model=raw.model,
            provider=raw.provider,
            prompt_tokens=usage.prompt_tokens if usage else 0,
            completion_tokens=usage.completion_tokens if usage else 0,
            total_tokens=usage.total_tokens if usage else 0,
            finish_reason=raw.finish_reason,
        )

    async def aclose(self) -> None:
        await self._client.aclose()
