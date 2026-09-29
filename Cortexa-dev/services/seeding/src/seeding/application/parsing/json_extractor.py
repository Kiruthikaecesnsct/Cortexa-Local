import json
import re

from seeding.domain.errors.seeding_errors import OpportunityParseError

_FENCE_PATTERN = re.compile(r"```(?:json)?\s*(\{.*\})\s*```", re.DOTALL)


def _extract_from_prose(text: str) -> str:
    start = text.find("{")
    end = text.rfind("}")
    if start == -1 or end == -1 or end <= start:
        raise OpportunityParseError(f"no JSON object found in response: {text[:120]!r}")
    return text[start : end + 1]


def extract_json(content: str) -> dict:
    match = _FENCE_PATTERN.search(content)
    candidate = match.group(1) if match else _extract_from_prose(content.strip())
    try:
        result = json.loads(candidate)
    except json.JSONDecodeError as exc:
        raise OpportunityParseError(f"invalid JSON in response: {exc}") from exc
    if not isinstance(result, dict):
        raise OpportunityParseError(f"expected JSON object, got {type(result).__name__}")
    return result
