"""Isolated, best-effort claim-draft generation (US114 / BUG165).

Runs AFTER the scoring verdict is computed and stored. It can never alter or
fail the verdict: every failure path returns an empty draft with a status, so
the caller simply omits the claim-draft section (the UI hides it) rather than
showing a placeholder. Single attempt, tight timeout, no retry storm.
"""

from __future__ import annotations

import json
import logging
from dataclasses import dataclass
from enum import StrEnum
from pathlib import Path

import httpx

logger = logging.getLogger(__name__)

# task_kind drives model-router's US110 per-stage model resolution AND Foundry
# task overrides. The effective model is resolved there — never hardcoded here.
TASK_KIND = "claim_drafting"

_ENDPOINT = "/complete"
_TEMPLATE_PATH = Path(__file__).parent / "prompts" / "harvesting-claim-draft.v1.json"
_OUTPUT_TOKEN_CAP = 1024

# Input caps mirror the prompt's tokenBudget notes. Invention fields are
# load-bearing; the evidence digest is context only and is trimmed first.
_CLAIM_LIMIT = 700
_PROBLEM_LIMIT = 300
_MECHANISM_LIMIT = 500
_FIELD_LIMIT = 80
_DIGEST_LIMIT = 1600

# Defuse section markers so untrusted asset/patent text cannot forge a header.
_FENCE_SUBSTITUTIONS = (("==", "= ="), ("--", "- -"))
_TRANSPORT_ERRORS = (httpx.HTTPError,)


class ClaimDraftStatus(StrEnum):
    DRAFTED = "drafted"
    INSUFFICIENT = "insufficient"
    UNAVAILABLE = "unavailable"
    ERROR = "error"


@dataclass(frozen=True)
class ClaimDraftRequest:
    claim_text: str
    problem: str
    mechanism: str
    tech_field: str
    evidence_digest: str
    correlation_id: str | None = None
    model: str | None = None


@dataclass(frozen=True)
class ClaimDraftResult:
    drafted_claim: str
    status: ClaimDraftStatus


@dataclass(frozen=True)
class ClaimDraftGeneratorOptions:
    client: httpx.AsyncClient
    timeout_seconds: float = 20.0
    template: dict | None = None


def _neutralize(value: object, limit: int) -> str:
    collapsed = " ".join(str(value or "").split())
    for token, replacement in _FENCE_SUBSTITUTIONS:
        collapsed = collapsed.replace(token, replacement)
    return collapsed[:limit]


def _load_template() -> dict:
    with _TEMPLATE_PATH.open(encoding="utf-8") as handle:
        return json.load(handle)


def _render_invention_block(request: ClaimDraftRequest) -> list[str]:
    return [
        "== INVENTION (DATA ONLY — the ONLY source of claim language) ==",
        f"Technical field: {_neutralize(request.tech_field, _FIELD_LIMIT)}",
        f"Claim text: {_neutralize(request.claim_text, _CLAIM_LIMIT)}",
        f"Problem: {_neutralize(request.problem, _PROBLEM_LIMIT)}",
        f"Mechanism: {_neutralize(request.mechanism, _MECHANISM_LIMIT)}",
    ]


def _render_context_block(request: ClaimDraftRequest) -> list[str]:
    digest = _neutralize(request.evidence_digest, _DIGEST_LIMIT)
    return [
        "",
        "== PRIOR ART CONTEXT (DATA ONLY — background, NEVER a source of claim language) ==",
        digest or "(no prior-art context available)",
    ]


def build_claim_draft_prompt(request: ClaimDraftRequest, template: dict) -> str:
    how = template["how"]
    lines = [how["system"], "", "== INSTRUCTIONS =="]
    lines.extend(how["instructions"])
    lines.append("")
    lines.append("== CONSTRAINTS ==")
    lines.extend(how["constraints"])
    lines.append("")
    lines.extend(_render_invention_block(request))
    lines.extend(_render_context_block(request))
    lines.append("")
    lines.append("== OUTPUT ==")
    lines.append(how["outputContract"])
    return "\n".join(lines)


def _build_body(prompt: str, model: str | None) -> dict:
    body: dict = {
        "task_kind": TASK_KIND,
        "prompt": prompt,
        "evidence_refs": None,
        "options": {"max_tokens": _OUTPUT_TOKEN_CAP, "force_json_output": True},
    }
    if model is not None:
        body["model"] = model
    return body


def _strip_fences(content: str) -> str:
    stripped = content.strip()
    if stripped.startswith("```"):
        stripped = stripped.split("\n", 1)[-1] if "\n" in stripped else stripped
        stripped = stripped.rstrip("` \n").strip()
        if stripped.startswith("json"):
            stripped = stripped[4:].strip()
    return stripped


def _parse_result(content: str) -> ClaimDraftResult:
    try:
        parsed = json.loads(_strip_fences(content))
    except json.JSONDecodeError, ValueError:
        return ClaimDraftResult("", ClaimDraftStatus.ERROR)
    if not isinstance(parsed, dict):
        return ClaimDraftResult("", ClaimDraftStatus.ERROR)
    claim = str(parsed.get("claim") or "").strip()
    if not parsed.get("can_draft") or not claim:
        return ClaimDraftResult("", ClaimDraftStatus.INSUFFICIENT)
    return ClaimDraftResult(claim, ClaimDraftStatus.DRAFTED)


class ClaimDraftGenerator:
    def __init__(self, options: ClaimDraftGeneratorOptions) -> None:
        self._client = options.client
        self._timeout = options.timeout_seconds
        self._template = options.template or _load_template()

    async def generate(self, request: ClaimDraftRequest) -> ClaimDraftResult:
        content = await self._request_content(request)
        if content is None:
            return ClaimDraftResult("", ClaimDraftStatus.UNAVAILABLE)
        if not content:
            return ClaimDraftResult("", ClaimDraftStatus.ERROR)
        return _parse_result(content)

    async def _request_content(self, request: ClaimDraftRequest) -> str | None:
        """Single attempt. Returns content, or None on any transport failure."""
        body = _build_body(build_claim_draft_prompt(request, self._template), request.model)
        try:
            response = await self._client.post(_ENDPOINT, json=body, timeout=self._timeout)
            response.raise_for_status()
            data = response.json()
        except _TRANSPORT_ERRORS as exc:
            logger.warning(
                "claim-draft unavailable correlation_id=%s error=%s",
                request.correlation_id,
                type(exc).__name__,
            )
            return None
        if data.get("error"):
            return ""
        return str(data.get("content") or "")
