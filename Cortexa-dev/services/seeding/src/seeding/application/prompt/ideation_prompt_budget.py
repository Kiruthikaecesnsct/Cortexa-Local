import math
from collections.abc import Callable
from dataclasses import replace

from seeding.application.prompt.ideation_prompt_builder import (
    CritiqueInputs,
    ProposeInputs,
    RefineInputs,
    build_critique_prompt,
    build_propose_prompt,
    build_refine_prompt,
)

_CHARS_PER_TOKEN = 4
_BRIEF_TRIM_ORDER = ("tech_fields", "key_concepts", "contributions", "future_work", "limitations")
_CRITIQUE_TRIM_ORDER = ("crowded", "accepted", "contributions")


def estimate_tokens(text: str) -> int:
    return math.ceil(len(text) / _CHARS_PER_TOKEN)


def _drop_last(items: list) -> list | None:
    return items[:-1] if items else None


def _trim_brief(brief):
    for field_name in _BRIEF_TRIM_ORDER:
        remaining = _drop_last(getattr(brief, field_name))
        if remaining is not None:
            return brief.model_copy(update={field_name: remaining})
    return None


def _shrink_propose(inputs: ProposeInputs) -> ProposeInputs | None:
    excerpts = _drop_last(inputs.excerpts)
    if excerpts is not None:
        return replace(inputs, excerpts=excerpts)
    brief = _trim_brief(inputs.digest)
    if brief is not None:
        return replace(inputs, digest=brief)
    return None


def _shrink_refine(inputs: RefineInputs) -> RefineInputs | None:
    excerpts = _drop_last(inputs.excerpts)
    if excerpts is None:
        return None
    return replace(inputs, excerpts=excerpts)


def _shrink_critique(inputs: CritiqueInputs) -> CritiqueInputs | None:
    for field_name in _CRITIQUE_TRIM_ORDER:
        remaining = _drop_last(getattr(inputs, field_name))
        if remaining is not None:
            return replace(inputs, **{field_name: remaining})
    return None


def _fit_inputs(inputs, max_tokens: int, render: Callable, shrink: Callable):
    while estimate_tokens(render(inputs)) > max_tokens:
        trimmed = shrink(inputs)
        if trimmed is None:
            return inputs
        inputs = trimmed
    return inputs


def fit_propose_prompt(inputs: ProposeInputs, max_tokens: int) -> tuple[str, set[str]]:
    fitted = _fit_inputs(inputs, max_tokens, lambda i: build_propose_prompt(i)[0], _shrink_propose)
    return build_propose_prompt(fitted)


def fit_critique_prompt(inputs: CritiqueInputs, max_tokens: int) -> str:
    fitted = _fit_inputs(inputs, max_tokens, build_critique_prompt, _shrink_critique)
    return build_critique_prompt(fitted)


def fit_refine_prompt(inputs: RefineInputs, max_tokens: int) -> str:
    fitted = _fit_inputs(inputs, max_tokens, build_refine_prompt, _shrink_refine)
    return build_refine_prompt(fitted)
