import logging

import pytest

from seeding.domain.ports.model_router_port import ModelResult
from seeding.infrastructure.observability.llm_telemetry import (
    LlmCallTags,
    _build_fields,
    llm_call_span,
)

STAGE = "ideation_propose"
TASK_KIND = "seeding"
BATCH_ID = "batch-tel"
DOC_ID = "doc-tel"
CORR_ID = "corr-tel"


def _tags(stage: str = STAGE, round_index: int | None = 2) -> LlmCallTags:
    return LlmCallTags(
        stage=stage,
        task_kind=TASK_KIND,
        batch_id=BATCH_ID,
        document_id=DOC_ID,
        correlation_id=CORR_ID,
        model_requested="gpt-5.5",
        max_output_tokens=4096,
        round_index=round_index,
    )


def _result() -> ModelResult:
    return ModelResult(
        content="c",
        citations=[],
        model="gpt-5.5-2026",
        provider="Foundry",
        prompt_tokens=11,
        completion_tokens=22,
        total_tokens=33,
        finish_reason="stop",
    )


def test_build_fields_maps_result_and_tags():
    fields = _build_fields(_tags(), _result(), wall_time_ms=150, ok=True, error_class=None)

    assert fields["event"] == "llm_call"
    assert fields["service"] == "seeding"
    assert fields["stage"] == STAGE
    assert fields["task_kind"] == TASK_KIND
    assert fields["model_requested"] == "gpt-5.5"
    assert fields["model_used"] == "gpt-5.5-2026"
    assert fields["provider"] == "Foundry"
    assert fields["batch_id"] == BATCH_ID
    assert fields["document_id"] == DOC_ID
    assert fields["correlation_id"] == CORR_ID
    assert fields["round_index"] == 2
    assert fields["prompt_tokens"] == 11
    assert fields["completion_tokens"] == 22
    assert fields["total_tokens"] == 33
    assert fields["max_output_tokens"] == 4096
    assert fields["wall_time_ms"] == 150
    assert fields["finish_reason"] == "stop"
    assert fields["ok"] is True
    assert fields["error_class"] is None


def test_build_fields_none_result_zero_tokens_and_off_ideation_round_none():
    fields = _build_fields(
        _tags(stage="report", round_index=None),
        None,
        wall_time_ms=5,
        ok=False,
        error_class="ModelRouterFailedError",
    )

    assert fields["round_index"] is None
    assert fields["prompt_tokens"] == 0
    assert fields["completion_tokens"] == 0
    assert fields["total_tokens"] == 0
    assert fields["model_used"] == ""
    assert fields["provider"] == ""
    assert fields["finish_reason"] is None
    assert fields["ok"] is False
    assert fields["error_class"] == "ModelRouterFailedError"


@pytest.mark.asyncio
async def test_span_emits_single_success_record(caplog):
    logger = logging.getLogger("test.telemetry.success")
    with caplog.at_level(logging.INFO, logger="test.telemetry.success"):
        async with llm_call_span(logger, _tags()) as span:
            span.result = _result()

    records = [r for r in caplog.records if r.message == "llm_call"]
    assert len(records) == 1
    record = records[0]
    assert record.ok is True
    assert record.error_class is None
    assert record.total_tokens == 33
    assert record.stage == STAGE


@pytest.mark.asyncio
async def test_span_records_error_class_and_reraises_on_exception(caplog):
    logger = logging.getLogger("test.telemetry.error")

    with caplog.at_level(logging.INFO, logger="test.telemetry.error"):
        with pytest.raises(ValueError):
            async with llm_call_span(logger, _tags()) as span:
                span.result = None
                raise ValueError("boom")

    records = [r for r in caplog.records if r.message == "llm_call"]
    assert len(records) == 1
    record = records[0]
    assert record.ok is False
    assert record.error_class == "ValueError"
    assert record.prompt_tokens == 0
