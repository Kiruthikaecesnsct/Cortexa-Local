import asyncio
import logging
from dataclasses import dataclass

import httpx
from tenacity import RetryError, retry, retry_if_exception, stop_after_attempt, stop_after_delay

from extraction.application.dtos.extraction_prompt import ExtractionPrompt
from extraction.application.dtos.model_complete_result import (
    DualModelCompleteResult,
    ModelCompleteResult,
)
from extraction.domain.enums.model_mode import ModelMode
from extraction.domain.errors.extraction_errors import ModelCallFailed

logger = logging.getLogger(__name__)


@dataclass(frozen=True)
class RetryConfig:
    max_retries: int
    backoff_max: float
    honor_retry_after: bool
    total_budget: float


def _is_transient(exc: BaseException) -> bool:
    if isinstance(exc, asyncio.CancelledError):
        return False
    if isinstance(exc, httpx.NetworkError):
        return True
    if isinstance(exc, httpx.TimeoutException):
        return True
    if isinstance(exc, httpx.HTTPStatusError):
        return exc.response.status_code in {429, 500, 502, 503, 504}
    return False


def _parse_retry_after(header_value: str | None) -> float | None:
    if header_value is None:
        return None
    try:
        return float(header_value)
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
    parsed = _parse_retry_after(response.headers.get("Retry-After"))
    if parsed is None:
        return None
    return max(min(parsed, cap), 0.0)


def _compute_exponential_delay(attempt: int, cap: float) -> float:
    return min(2 ** (attempt - 2), cap)


def _build_payload(prompt: ExtractionPrompt, mode: ModelMode, ai_model: str | None = None) -> dict:
    payload: dict = {
        "task_kind": prompt.task_kind,
        "prompt": prompt.prompt,
        "mode": mode.value,
    }
    if prompt.evidence_refs is not None:
        payload["evidence_refs"] = prompt.evidence_refs
    payload["options"] = {
        "max_tokens": prompt.options.max_tokens,
        "temperature": prompt.options.temperature,
        "force_json_output": True,
    }
    if ai_model is not None:
        payload["model"] = ai_model
    return payload


async def _execute_post(client: httpx.AsyncClient, url: str, payload: dict, timeout: float) -> dict:
    try:
        response = await client.post(url, json=payload, timeout=timeout)
        response.raise_for_status()
        logger.info("model-router response status=%s url=%s", response.status_code, url)
        return response.json()
    except asyncio.CancelledError:
        raise
    except Exception as exc:
        if _is_transient(exc):
            raise
        if isinstance(exc, httpx.HTTPStatusError):
            error_code, categories = _extract_error_details(exc.response)
            raise ModelCallFailed(
                f"model-router returned {exc.response.status_code}",
                status_code=exc.response.status_code,
                error_code=error_code,
                categories=categories,
            ) from exc
        raise ModelCallFailed(str(exc)) from exc


def _extract_error_details(response: httpx.Response) -> tuple[str | None, list[str] | None]:
    try:
        body = response.json()
        error_code = body.get("code")
        categories = body.get("categories")
        if error_code is None:
            extensions = body.get("extensions", {})
            error_code = extensions.get("code")
            if categories is None:
                categories = extensions.get("categories")
        if categories is not None and not isinstance(categories, list):
            categories = None
        return error_code, categories
    except Exception:
        return None, None


def build_retry_with_backoff_cap(config: RetryConfig):
    def _smart_wait(retry_state):
        attempt = retry_state.attempt_number
        if attempt == 1:
            return 0
        if not config.honor_retry_after:
            return _compute_exponential_delay(attempt, config.backoff_max)
        response = _extract_response_from_outcome(retry_state.outcome)
        delay = _extract_retry_after_delay(response, config.backoff_max)
        if delay is not None:
            return delay
        return _compute_exponential_delay(attempt, config.backoff_max)

    return retry(
        retry=retry_if_exception(_is_transient),
        stop=stop_after_attempt(config.max_retries) | stop_after_delay(config.total_budget),
        wait=_smart_wait,
        reraise=False,
    )


class ModelRouterClient:
    def __init__(self, http_client: httpx.AsyncClient, base_url: str, timeout: float) -> None:
        self._client = http_client
        self._base_url = base_url.rstrip("/")
        self._timeout = timeout
        self._retry_config = RetryConfig(
            max_retries=3,
            backoff_max=10.0,
            honor_retry_after=True,
            total_budget=120.0,
        )

    async def complete(
        self, prompt: ExtractionPrompt, mode: ModelMode, ai_model: str | None = None
    ) -> ModelCompleteResult:
        raw = await self._post("/complete", prompt, mode, ai_model)
        return ModelCompleteResult.model_validate(self._normalise(raw))

    async def complete_dual(
        self, prompt: ExtractionPrompt, ai_model: str | None = None
    ) -> DualModelCompleteResult:
        raw = await self._post("/complete/dual", prompt, ModelMode.dual_adversarial, ai_model)
        return DualModelCompleteResult(
            primary=ModelCompleteResult.model_validate(self._normalise(raw["Primary"])),
            secondary=ModelCompleteResult.model_validate(self._normalise(raw["Secondary"])),
        )

    async def call(
        self, prompt: ExtractionPrompt, mode: ModelMode, ai_model: str | None = None
    ) -> ModelCompleteResult | DualModelCompleteResult:
        if mode is ModelMode.dual_adversarial:
            return await self.complete_dual(prompt, ai_model)
        return await self.complete(prompt, mode, ai_model)

    async def _post(
        self, path: str, prompt: ExtractionPrompt, mode: ModelMode, ai_model: str | None = None
    ) -> dict:
        url = f"{self._base_url}{path}"
        attempt_fn = self._make_attempt(url, prompt, mode, ai_model)
        try:
            return await attempt_fn()
        except RetryError as exc:
            raise ModelCallFailed("All retry attempts exhausted") from exc

    def _make_attempt(
        self, url: str, prompt: ExtractionPrompt, mode: ModelMode, ai_model: str | None = None
    ):
        retry_decorator = build_retry_with_backoff_cap(self._retry_config)
        payload = _build_payload(prompt, mode, ai_model)

        @retry_decorator
        async def _attempt() -> dict:
            return await _execute_post(self._client, url, payload, self._timeout)

        return _attempt

    @staticmethod
    def _normalise(raw: dict) -> dict:
        return {k.lower(): v for k, v in raw.items()}

    @classmethod
    def with_retries(
        cls,
        http_client: httpx.AsyncClient,
        base_url: str,
        timeout: float,
        retry_config: RetryConfig,
    ) -> ModelRouterClient:
        instance = cls(http_client, base_url, timeout)
        instance._retry_config = retry_config
        return instance
