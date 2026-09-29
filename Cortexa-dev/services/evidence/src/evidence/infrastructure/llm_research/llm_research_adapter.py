import asyncio
import json
import logging
import re

import httpx
from pydantic import ValidationError

from evidence.domain.errors.evidence_errors import LlmResearchError
from evidence.domain.models.research_finding import ResearchFinding
from evidence.infrastructure.config.settings import EvidenceSettings
from evidence.infrastructure.llm_research.prompt_builder import build_research_prompt
from evidence.infrastructure.patent_apis.patent_adapter import build_retry_with_backoff_cap

logger = logging.getLogger(__name__)

_REQUEST_BODY_TEMPLATE = {
    "task_kind": "patent_research",
    "evidence_refs": None,
    "options": None,
}

_CONTENT_FILTER_MARKERS = (
    "content_filter",
    "content management policy",
    "responsibleaipolicyviolation",
    "content_filter_result",
)


def _text_has_content_filter_marker(text: str) -> bool:
    lowered = text.lower()
    return any(marker in lowered for marker in _CONTENT_FILTER_MARKERS)


def _error_json_has_content_filter(error_json: dict) -> bool:
    error = error_json.get("error")
    if not isinstance(error, dict):
        return False
    if error.get("code") == "content_filter":
        return True
    inner = error.get("innererror")
    if not isinstance(inner, dict):
        return False
    return inner.get("code") == "ResponsibleAIPolicyViolation" or "content_filter_result" in inner


def _is_content_filter_response(data: dict) -> bool:
    if data.get("finish_reason") == "content_filter":
        return True
    error = data.get("error")
    return isinstance(error, str) and _text_has_content_filter_marker(error)


def _is_content_filter_http_error(exc: httpx.HTTPStatusError) -> bool:
    try:
        error_json = exc.response.json()
    except json.JSONDecodeError:
        error_json = None
    if isinstance(error_json, dict) and _error_json_has_content_filter(error_json):
        return True
    fallback_text = exc.response.text or str(exc)
    return _text_has_content_filter_marker(fallback_text)


def _clamp_unit(value: float) -> float:
    return max(0.0, min(1.0, value))


def _normalize_citation(entry: object, fallback_confidence: float) -> object:
    """Coerce one citation to the {id, confidence} shape.

    Legacy responses return bare id strings; those fall back to the finding-level
    confidence. Object responses keep their own confidence, clamped to [0, 1],
    and fall back when the confidence is missing or non-numeric.
    """
    if isinstance(entry, str):
        return {"id": entry, "confidence": fallback_confidence}
    if isinstance(entry, dict):
        raw_confidence = entry.get("confidence", fallback_confidence)
        try:
            confidence = _clamp_unit(float(raw_confidence))
        except TypeError, ValueError:
            confidence = fallback_confidence
        return {"id": entry.get("id"), "confidence": confidence}
    return entry


def _normalize_citations(raw: object, fallback_confidence: float) -> object:
    if not isinstance(raw, list):
        return raw
    return [_normalize_citation(entry, fallback_confidence) for entry in raw]


def _parse_content(raw_content: str) -> ResearchFinding:
    stripped = raw_content.strip()
    if stripped.startswith("```"):
        stripped = re.sub(r"^```[a-z]*\s*", "", stripped)
        stripped = stripped.rstrip("` \n").strip()
    try:
        parsed = json.loads(stripped)
    except json.JSONDecodeError as exc:
        raise LlmResearchError("model returned non-JSON content") from exc
    fallback_confidence = 0.0
    if isinstance(parsed, dict):
        if "confidence" in parsed:
            parsed["confidence"] = _clamp_unit(parsed["confidence"])
            fallback_confidence = parsed["confidence"]
        if "citations" in parsed:
            parsed["citations"] = _normalize_citations(parsed["citations"], fallback_confidence)
    try:
        return ResearchFinding(**parsed)
    except ValidationError as exc:
        raise LlmResearchError(f"model returned invalid research JSON: {exc}") from exc


class LlmResearchAdapter:
    def __init__(self, settings: EvidenceSettings, client: httpx.AsyncClient | None = None) -> None:
        self._settings = settings
        self._client = client or httpx.AsyncClient(
            timeout=httpx.Timeout(settings.llm_research_timeout_seconds)
        )
        self._semaphore = asyncio.Semaphore(settings.llm_research_max_concurrency)
        self._retry = build_retry_with_backoff_cap(
            settings.llm_research_max_retries,
            settings.llm_research_backoff_max_seconds,
            settings.llm_research_honor_retry_after,
            settings.llm_research_total_retry_budget_seconds,
        )

    async def research(
        self,
        candidate_description: str | None,
        source_text: str | None,
        model: str | None = None,
    ) -> ResearchFinding:
        desc = (candidate_description or "").strip()
        src = (source_text or "").strip()
        if not desc and not src:
            raise LlmResearchError(
                "LLM research skipped: no input"
                " (both candidate_description and source_text are empty)"
            )
        try:
            decorated = self._retry(self._execute)
            return await decorated(desc, src, model)
        except (httpx.HTTPStatusError, httpx.TimeoutException, httpx.NetworkError) as exc:
            status_code = (
                exc.response.status_code if isinstance(exc, httpx.HTTPStatusError) else None
            )
            content_filter = isinstance(
                exc, httpx.HTTPStatusError
            ) and _is_content_filter_http_error(exc)
            error_detail = str(exc) or type(exc).__name__
            raise LlmResearchError(
                f"LLM research request failed: {error_detail}",
                status_code=status_code,
                content_filter=content_filter,
            ) from exc

    async def _execute(
        self,
        candidate_description: str,
        source_text: str,
        model: str | None = None,
    ) -> ResearchFinding:
        async with self._semaphore:
            return await self._call_single(candidate_description, source_text, model)

    def _build_request_body(self, prompt: str, model: str | None) -> dict:
        body = {**_REQUEST_BODY_TEMPLATE, "prompt": prompt}
        if model is not None:
            body["model"] = model
        return body

    async def _call_single(
        self, candidate_description: str, source_text: str, model: str | None = None
    ) -> ResearchFinding:
        prompt = build_research_prompt(candidate_description, source_text)
        url = f"{self._settings.model_router_url}/complete"
        body = self._build_request_body(prompt, model)
        response = await self._client.post(url, json=body)
        response.raise_for_status()
        data = response.json()
        if _is_content_filter_response(data):
            raise LlmResearchError(
                "LLM research response was content-filtered", content_filter=True
            )
        if data.get("error"):
            raise LlmResearchError(str(data["error"]))
        return _parse_content(data["content"])
