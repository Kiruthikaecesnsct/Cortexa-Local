from uuid import UUID

_STRIP_CHARS = " \t\r\n[](){}\"'`,"


def normalize_candidate_id(raw: object) -> str:
    """Canonicalize a candidate id for tolerant grounding comparison.

    Strips surrounding whitespace and stray punctuation (brackets, quotes,
    commas, backticks) that the model can bleed into an echoed id, then
    lower-cases. If the cleaned value parses as a UUID it is returned in
    canonical 36-char form; otherwise the cleaned string is returned as-is
    so non-UUID ids (e.g. test fixtures) still match.
    """
    cleaned = str(raw or "").strip(_STRIP_CHARS).lower()
    try:
        return str(UUID(cleaned))
    except ValueError:
        return cleaned
