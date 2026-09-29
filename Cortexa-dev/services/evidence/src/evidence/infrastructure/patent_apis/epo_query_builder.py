import re

_STOPWORDS = frozenset(
    {
        "a",
        "an",
        "and",
        "are",
        "as",
        "at",
        "be",
        "by",
        "for",
        "from",
        "has",
        "he",
        "in",
        "is",
        "it",
        "its",
        "of",
        "on",
        "that",
        "the",
        "to",
        "was",
        "will",
        "with",
    }
)


def build_epo_cql_query(
    text: str,
    max_terms: int,
    max_length: int,
) -> str:
    tokens = _tokenize(text)
    filtered = _filter_stopwords(tokens)
    unique = _deduplicate(filtered)
    capped = _cap_terms(unique, max_terms)
    query = _format_cql(capped)
    return _enforce_length_limit(query, capped, max_length)


def _tokenize(text: str) -> list[str]:
    words = re.findall(r"\w+", text.lower())
    return [w for w in words if len(w) >= 3]


def _filter_stopwords(tokens: list[str]) -> list[str]:
    return [t for t in tokens if t not in _STOPWORDS]


def _deduplicate(tokens: list[str]) -> list[str]:
    seen = set()
    result = []
    for token in tokens:
        if token not in seen:
            seen.add(token)
            result.append(token)
    return result


def _cap_terms(tokens: list[str], max_terms: int) -> list[str]:
    return tokens[:max_terms]


def _format_cql(tokens: list[str]) -> str:
    if not tokens:
        return ""
    terms = " OR ".join(f'"{t}"' for t in tokens)
    return f"txt=({terms})"


def _enforce_length_limit(query: str, tokens: list[str], max_length: int) -> str:
    if len(query) <= max_length:
        return query

    for term_count in range(len(tokens) - 1, 0, -1):
        reduced = _format_cql(tokens[:term_count])
        if len(reduced) <= max_length:
            return reduced

    return ""
