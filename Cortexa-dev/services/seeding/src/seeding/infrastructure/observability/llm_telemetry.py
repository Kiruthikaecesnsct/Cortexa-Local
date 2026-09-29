import logging
import time
from contextlib import asynccontextmanager
from dataclasses import dataclass

_SERVICE = "seeding"
_EVENT = "llm_call"


@dataclass(frozen=True)
class LlmCallTags:
    stage: str
    task_kind: str
    batch_id: str
    document_id: str
    correlation_id: str | None
    model_requested: str | None
    max_output_tokens: int
    round_index: int | None = None


class SpanResult:
    def __init__(self) -> None:
        self.result: object | None = None


def _token_fields(result: object | None) -> dict:
    if result is None:
        return {"prompt_tokens": 0, "completion_tokens": 0, "total_tokens": 0}
    return {
        "prompt_tokens": result.prompt_tokens,
        "completion_tokens": result.completion_tokens,
        "total_tokens": result.total_tokens,
    }


def _model_fields(result: object | None) -> dict:
    if result is None:
        return {"model_used": "", "provider": "", "finish_reason": None}
    return {
        "model_used": result.model,
        "provider": result.provider,
        "finish_reason": result.finish_reason,
    }


def _build_fields(
    tags: LlmCallTags,
    result: object | None,
    wall_time_ms: int,
    ok: bool,
    error_class: str | None,
) -> dict:
    fields = {
        "event": _EVENT,
        "service": _SERVICE,
        "stage": tags.stage,
        "task_kind": tags.task_kind,
        "model_requested": tags.model_requested,
        "batch_id": tags.batch_id,
        "document_id": tags.document_id,
        "correlation_id": tags.correlation_id,
        "round_index": tags.round_index,
        "max_output_tokens": tags.max_output_tokens,
        "wall_time_ms": wall_time_ms,
        "ok": ok,
        "error_class": error_class,
    }
    fields.update(_model_fields(result))
    fields.update(_token_fields(result))
    return fields


@asynccontextmanager
async def llm_call_span(logger: logging.Logger, tags: LlmCallTags):
    box = SpanResult()
    start = time.perf_counter()
    ok = True
    error_class: str | None = None
    try:
        yield box
    except BaseException as exc:
        ok = False
        error_class = type(exc).__name__
        raise
    finally:
        wall_time_ms = int((time.perf_counter() - start) * 1000)
        fields = _build_fields(tags, box.result, wall_time_ms, ok, error_class)
        logger.info(_EVENT, extra=fields)
