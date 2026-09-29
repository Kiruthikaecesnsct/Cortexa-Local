_PROMPT_DELIMITERS = {"\n", "\r", "==", "--"}


def contains_prompt_delimiter(value: str) -> bool:
    return any(delimiter in value for delimiter in _PROMPT_DELIMITERS)


def reject_prompt_delimiters(value: object) -> object:
    if isinstance(value, str) and contains_prompt_delimiter(value):
        raise ValueError("field must not contain newlines, '==', or '--'")
    return value


_FENCE_SUBSTITUTIONS = (("==", "= ="), ("--", "- -"))


def neutralize_untrusted(value: str) -> str:
    """Make untrusted grounding text safe to embed in a prompt.

    Rejecting delimiters is wrong for real source text (it legitimately contains
    newlines and dashes). Instead collapse all whitespace to single spaces — so
    injected content cannot start a new logical prompt line — and defuse the
    '==' / '--' section markers so it cannot forge a header.
    """
    collapsed = " ".join(value.split())
    for token, replacement in _FENCE_SUBSTITUTIONS:
        collapsed = collapsed.replace(token, replacement)
    return collapsed
