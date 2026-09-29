import re

_DEFAULT_MAX_CHARS = 600
_ELLIPSIS = "…"
_SENTENCE_END_RE = re.compile(r"[.!?][\"'”’)\]]?(?=\s|$)")
_WHITESPACE_RUN_RE = re.compile(r"\s+")


def build_clean_excerpt(
    text: str, span_start: int, span_end: int, max_chars: int = _DEFAULT_MAX_CHARS
) -> str:
    if not text:
        return ""
    start, end = _clamp_span(text, span_start, span_end)
    expanded_start = _expand_to_sentence_start(text, start)
    expanded_end = _expand_to_sentence_end(text, end)
    normalized = _normalize_whitespace(text[expanded_start:expanded_end])
    return _cap_length(normalized, max_chars)


def _clamp_span(text: str, span_start: int, span_end: int) -> tuple[int, int]:
    length = len(text)
    start = max(0, min(span_start, length))
    end = max(start, min(span_end, length))
    return start, end


def _expand_to_sentence_start(text: str, start: int) -> int:
    preceding = text[:start]
    last_match = None
    for match in _SENTENCE_END_RE.finditer(preceding):
        last_match = match
    if last_match is None:
        return 0
    candidate = last_match.end()
    while candidate < len(text) and text[candidate].isspace():
        candidate += 1
    return min(candidate, start)


def _expand_to_sentence_end(text: str, end: int) -> int:
    for match in _SENTENCE_END_RE.finditer(text):
        if match.end() >= end:
            return match.end()
    return len(text)


def _normalize_whitespace(text: str) -> str:
    return _WHITESPACE_RUN_RE.sub(" ", text).strip()


def _cap_length(text: str, max_chars: int) -> str:
    if len(text) <= max_chars:
        return text
    truncated = text[:max_chars]
    last_space = truncated.rfind(" ")
    if last_space != -1:
        truncated = truncated[:last_space]
    return truncated.rstrip() + _ELLIPSIS
