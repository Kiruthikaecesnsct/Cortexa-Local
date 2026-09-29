import json
import re

from scoring.domain.errors.scoring_errors import AxisParseError

_FENCE_PATTERN = re.compile(r"```(?:json)?\s*(\{.*?\})\s*```", re.DOTALL)


def _extract_from_prose(text: str) -> str:
    start = text.find("{")
    end = text.rfind("}")
    if start == -1 or end == -1 or end <= start:
        raise AxisParseError(f"no JSON object found in response: {text[:120]!r}")
    return text[start : end + 1]


def extract_json(raw: str) -> dict:
    match = _FENCE_PATTERN.search(raw)
    candidate = match.group(1) if match else _extract_from_prose(raw.strip())
    try:
        result = json.loads(candidate)
    except json.JSONDecodeError as exc:
        raise AxisParseError(f"invalid JSON in response: {exc}") from exc
    if not isinstance(result, dict):
        raise AxisParseError(f"expected JSON object, got {type(result).__name__}")
    return result
