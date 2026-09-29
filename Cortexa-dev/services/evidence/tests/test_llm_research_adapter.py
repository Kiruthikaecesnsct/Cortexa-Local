import asyncio

import httpx
import pytest
import respx

from evidence.domain.enums.evidence_source import EvidenceSource
from evidence.domain.errors.evidence_errors import LlmResearchError
from evidence.infrastructure.config.settings import EvidenceSettings
from evidence.infrastructure.llm_research.llm_research_adapter import LlmResearchAdapter
from evidence.infrastructure.llm_research.prompt_builder import build_research_prompt
from tests.conftest import MODEL_ROUTER_URL, VECTOR_ROUTER_URL

CANDIDATE_DESCRIPTION = "Sparse tensor quantization for neural net inference"
SOURCE_TEXT = "This paper presents a novel approach to reduce model size."

EXPECTED_SINGLE_FINDINGS = ["US11111111: related neural net"]
EXPECTED_SINGLE_CONFIDENCE = 0.82
EXPECTED_SINGLE_CITATIONS = ["US11111111"]


@pytest.mark.asyncio
async def test_research_single_success(llm_research_settings, mock_model_router):
    adapter = LlmResearchAdapter(llm_research_settings)

    result = await adapter.research(CANDIDATE_DESCRIPTION, SOURCE_TEXT)

    assert result.findings == EXPECTED_SINGLE_FINDINGS
    assert result.confidence == pytest.approx(EXPECTED_SINGLE_CONFIDENCE)
    assert [c.id for c in result.citations] == EXPECTED_SINGLE_CITATIONS
    # Legacy string citations fall back to the finding-level confidence.
    assert result.citations[0].confidence == pytest.approx(EXPECTED_SINGLE_CONFIDENCE)
    assert result.source == EvidenceSource.LlmResearch


@pytest.mark.asyncio
async def test_research_markdown_json_stripped(
    llm_research_settings, mock_model_router_markdown_json
):
    adapter = LlmResearchAdapter(llm_research_settings)

    result = await adapter.research(CANDIDATE_DESCRIPTION, SOURCE_TEXT)

    assert result.findings == ["X"]
    assert result.confidence == pytest.approx(0.5)
    assert result.citations == []


@pytest.mark.asyncio
async def test_research_bad_json_raises(llm_research_settings, mock_model_router_bad_json):
    adapter = LlmResearchAdapter(llm_research_settings)

    with pytest.raises(LlmResearchError):
        await adapter.research(CANDIDATE_DESCRIPTION, SOURCE_TEXT)


@pytest.mark.asyncio
async def test_research_500_retries_then_raises(llm_research_settings, mock_model_router_500):
    settings = EvidenceSettings(
        model_router_url=MODEL_ROUTER_URL,
        vector_router_url=VECTOR_ROUTER_URL,
        llm_research_timeout_seconds=5.0,
        llm_research_max_retries=2,
        llm_research_max_concurrency=2,
    )
    adapter = LlmResearchAdapter(settings)

    with pytest.raises(LlmResearchError):
        await adapter.research(CANDIDATE_DESCRIPTION, SOURCE_TEXT)


@pytest.mark.asyncio
async def test_research_400_raises_immediately(llm_research_settings):
    with respx.mock(base_url=MODEL_ROUTER_URL) as mock:
        route = mock.post("/complete").mock(
            return_value=httpx.Response(400, json={"error": "bad request"})
        )
        adapter = LlmResearchAdapter(llm_research_settings)

        with pytest.raises(LlmResearchError):
            await adapter.research(CANDIDATE_DESCRIPTION, SOURCE_TEXT)

    assert route.call_count == 1


@pytest.mark.asyncio
async def test_research_error_in_body_raises(
    llm_research_settings, mock_model_router_error_in_body
):
    adapter = LlmResearchAdapter(llm_research_settings)

    with pytest.raises(LlmResearchError, match="quota exceeded"):
        await adapter.research(CANDIDATE_DESCRIPTION, SOURCE_TEXT)


@pytest.mark.asyncio
async def test_confidence_out_of_range_clamped(llm_research_settings):
    body = {
        "provider": "foundry",
        "model": "gpt-4o",
        "content": '{"findings": [], "confidence": 1.5, "citations": []}',
        "citations": None,
        "usage": None,
        "grounding": None,
        "error": None,
    }
    with respx.mock(base_url=MODEL_ROUTER_URL) as mock:
        mock.post("/complete").mock(return_value=httpx.Response(200, json=body))
        adapter = LlmResearchAdapter(llm_research_settings)

        result = await adapter.research(CANDIDATE_DESCRIPTION, SOURCE_TEXT)

    assert result.confidence == pytest.approx(1.0)


def test_prompt_contains_candidate_and_source():
    prompt = build_research_prompt(CANDIDATE_DESCRIPTION, SOURCE_TEXT)

    assert CANDIDATE_DESCRIPTION in prompt
    assert SOURCE_TEXT in prompt
    assert "JSON" in prompt


# ---------------------------------------------------------------------------
# Content-filter detection (BUG169)
# ---------------------------------------------------------------------------


@pytest.mark.asyncio
async def test_research_200_finish_reason_content_filter_raises_flagged_error(
    llm_research_settings,
):
    body = {
        "provider": "foundry",
        "model": "gpt-4o",
        "content": "",
        "finish_reason": "content_filter",
        "citations": None,
        "usage": None,
        "grounding": None,
        "error": None,
    }
    with respx.mock(base_url=MODEL_ROUTER_URL) as mock:
        mock.post("/complete").mock(return_value=httpx.Response(200, json=body))
        adapter = LlmResearchAdapter(llm_research_settings)

        with pytest.raises(LlmResearchError) as exc_info:
            await adapter.research(CANDIDATE_DESCRIPTION, SOURCE_TEXT)

    assert exc_info.value.content_filter is True


@pytest.mark.asyncio
async def test_research_200_error_string_content_filter_marker_raises_flagged_error(
    llm_research_settings,
):
    body = {
        "provider": "foundry",
        "model": "gpt-4o",
        "content": "",
        "citations": None,
        "usage": None,
        "grounding": None,
        "error": "Response blocked by content management policy",
    }
    with respx.mock(base_url=MODEL_ROUTER_URL) as mock:
        mock.post("/complete").mock(return_value=httpx.Response(200, json=body))
        adapter = LlmResearchAdapter(llm_research_settings)

        with pytest.raises(LlmResearchError) as exc_info:
            await adapter.research(CANDIDATE_DESCRIPTION, SOURCE_TEXT)

    assert exc_info.value.content_filter is True


@pytest.mark.asyncio
async def test_research_http_error_innererror_responsible_ai_policy_flags_content_filter(
    llm_research_settings,
):
    error_body = {
        "error": {
            "code": "internal_error",
            "innererror": {"code": "ResponsibleAIPolicyViolation"},
        }
    }
    with respx.mock(base_url=MODEL_ROUTER_URL) as mock:
        mock.post("/complete").mock(return_value=httpx.Response(400, json=error_body))
        adapter = LlmResearchAdapter(llm_research_settings)

        with pytest.raises(LlmResearchError) as exc_info:
            await adapter.research(CANDIDATE_DESCRIPTION, SOURCE_TEXT)

    assert exc_info.value.content_filter is True
    assert exc_info.value.status_code == 400


@pytest.mark.asyncio
async def test_research_http_error_content_filter_result_present_flags_content_filter(
    llm_research_settings,
):
    error_body = {
        "error": {
            "code": "internal_error",
            "innererror": {"content_filter_result": {"hate": {"filtered": True}}},
        }
    }
    with respx.mock(base_url=MODEL_ROUTER_URL) as mock:
        mock.post("/complete").mock(return_value=httpx.Response(400, json=error_body))
        adapter = LlmResearchAdapter(llm_research_settings)

        with pytest.raises(LlmResearchError) as exc_info:
            await adapter.research(CANDIDATE_DESCRIPTION, SOURCE_TEXT)

    assert exc_info.value.content_filter is True


@pytest.mark.asyncio
async def test_research_http_error_text_fallback_flags_content_filter(llm_research_settings):
    with respx.mock(base_url=MODEL_ROUTER_URL) as mock:
        mock.post("/complete").mock(
            return_value=httpx.Response(400, text="content_filter triggered upstream")
        )
        adapter = LlmResearchAdapter(llm_research_settings)

        with pytest.raises(LlmResearchError) as exc_info:
            await adapter.research(CANDIDATE_DESCRIPTION, SOURCE_TEXT)

    assert exc_info.value.content_filter is True


@pytest.mark.asyncio
async def test_research_400_without_content_filter_marker_not_flagged(llm_research_settings):
    with respx.mock(base_url=MODEL_ROUTER_URL) as mock:
        mock.post("/complete").mock(
            return_value=httpx.Response(400, json={"error": {"code": "bad_request"}})
        )
        adapter = LlmResearchAdapter(llm_research_settings)

        with pytest.raises(LlmResearchError) as exc_info:
            await adapter.research(CANDIDATE_DESCRIPTION, SOURCE_TEXT)

    assert exc_info.value.content_filter is False
    assert exc_info.value.status_code == 400


@pytest.mark.asyncio
async def test_research_url_built_from_model_router_url_field():
    custom_base = "http://custom-model-router"
    settings = EvidenceSettings(
        model_router_url=custom_base,
        vector_router_url="http://test-vector-router",
    )
    body = {
        "provider": "foundry",
        "model": "gpt-4o",
        "content": '{"findings": [], "confidence": 0.5, "citations": []}',
        "citations": None,
        "usage": None,
        "grounding": None,
        "error": None,
    }
    with respx.mock(base_url=custom_base) as mock:
        route = mock.post("/complete").mock(return_value=httpx.Response(200, json=body))
        adapter = LlmResearchAdapter(settings)

        await adapter.research(CANDIDATE_DESCRIPTION, SOURCE_TEXT)

    assert route.called


@pytest.mark.asyncio
async def test_semaphore_limits_concurrency(llm_research_settings):
    settings = EvidenceSettings(
        model_router_url=MODEL_ROUTER_URL,
        vector_router_url=VECTOR_ROUTER_URL,
        llm_research_timeout_seconds=5.0,
        llm_research_max_retries=1,
        llm_research_max_concurrency=2,
    )
    body = {
        "provider": "foundry",
        "model": "gpt-4o",
        "content": '{"findings": ["result"], "confidence": 0.8, "citations": []}',
        "citations": None,
        "usage": None,
        "grounding": None,
        "error": None,
    }
    with respx.mock(base_url=MODEL_ROUTER_URL) as mock:
        mock.post("/complete").mock(return_value=httpx.Response(200, json=body))
        adapter = LlmResearchAdapter(settings)

        results = await asyncio.wait_for(
            asyncio.gather(
                *[adapter.research(CANDIDATE_DESCRIPTION, SOURCE_TEXT) for _ in range(5)]
            ),
            timeout=10.0,
        )

    assert len(results) == 5
    assert all(r.findings == ["result"] for r in results)


@pytest.mark.asyncio
async def test_timeout_retries_bounded_by_max_retries():
    settings = EvidenceSettings(
        model_router_url=MODEL_ROUTER_URL,
        vector_router_url=VECTOR_ROUTER_URL,
        llm_research_timeout_seconds=0.1,
        llm_research_max_retries=2,
        llm_research_max_concurrency=1,
    )
    with respx.mock(base_url=MODEL_ROUTER_URL) as mock:
        route = mock.post("/complete").mock(side_effect=httpx.TimeoutException("timeout"))
        adapter = LlmResearchAdapter(settings)

        with pytest.raises(LlmResearchError):
            await adapter.research(CANDIDATE_DESCRIPTION, SOURCE_TEXT)

        assert route.call_count == 2


@pytest.mark.asyncio
async def test_timeout_exception_surfaces_as_llm_research_error():
    settings = EvidenceSettings(
        model_router_url=MODEL_ROUTER_URL,
        vector_router_url=VECTOR_ROUTER_URL,
        llm_research_timeout_seconds=0.1,
        llm_research_max_retries=1,
        llm_research_max_concurrency=1,
    )
    with respx.mock(base_url=MODEL_ROUTER_URL) as mock:
        mock.post("/complete").mock(side_effect=httpx.TimeoutException("request timeout"))
        adapter = LlmResearchAdapter(settings)

        with pytest.raises(LlmResearchError, match="LLM research request failed"):
            await adapter.research(CANDIDATE_DESCRIPTION, SOURCE_TEXT)


@pytest.mark.asyncio
async def test_429_honors_retry_after_then_succeeds():
    settings = EvidenceSettings(
        model_router_url=MODEL_ROUTER_URL,
        vector_router_url=VECTOR_ROUTER_URL,
        llm_research_timeout_seconds=10.0,
        llm_research_max_retries=3,
        llm_research_max_concurrency=1,
        llm_research_backoff_max_seconds=5.0,
        llm_research_honor_retry_after=True,
    )
    success_body = {
        "provider": "foundry",
        "model": "gpt-4o",
        "content": '{"findings": ["success"], "confidence": 0.9, "citations": ["US123"]}',
        "citations": None,
        "usage": None,
        "grounding": None,
        "error": None,
    }
    attempt = {"count": 0}

    def side_effect(request):
        attempt["count"] += 1
        if attempt["count"] <= 2:
            return httpx.Response(429, headers={"Retry-After": "0.5"}, json={"error": "rate limit"})
        return httpx.Response(200, json=success_body)

    with respx.mock(base_url=MODEL_ROUTER_URL) as mock:
        route = mock.post("/complete").mock(side_effect=side_effect)
        adapter = LlmResearchAdapter(settings)

        result = await adapter.research(CANDIDATE_DESCRIPTION, SOURCE_TEXT)

    assert result.findings == ["success"]
    assert result.confidence == pytest.approx(0.9)
    assert route.call_count == 3


@pytest.mark.asyncio
async def test_503_with_retry_after_then_succeeds():
    settings = EvidenceSettings(
        model_router_url=MODEL_ROUTER_URL,
        vector_router_url=VECTOR_ROUTER_URL,
        llm_research_timeout_seconds=10.0,
        llm_research_max_retries=3,
        llm_research_max_concurrency=1,
        llm_research_backoff_max_seconds=5.0,
        llm_research_honor_retry_after=True,
    )
    success_body = {
        "provider": "foundry",
        "model": "gpt-4o",
        "content": '{"findings": ["recovered"], "confidence": 0.85, "citations": []}',
        "citations": None,
        "usage": None,
        "grounding": None,
        "error": None,
    }
    attempt = {"count": 0}

    def side_effect(request):
        attempt["count"] += 1
        if attempt["count"] == 1:
            return httpx.Response(
                503, headers={"Retry-After": "1"}, json={"error": "service unavailable"}
            )
        return httpx.Response(200, json=success_body)

    with respx.mock(base_url=MODEL_ROUTER_URL) as mock:
        route = mock.post("/complete").mock(side_effect=side_effect)
        adapter = LlmResearchAdapter(settings)

        result = await adapter.research(CANDIDATE_DESCRIPTION, SOURCE_TEXT)

    assert result.findings == ["recovered"]
    assert route.call_count == 2


@pytest.mark.asyncio
async def test_504_retries_then_succeeds():
    settings = EvidenceSettings(
        model_router_url=MODEL_ROUTER_URL,
        vector_router_url=VECTOR_ROUTER_URL,
        llm_research_timeout_seconds=10.0,
        llm_research_max_retries=4,
        llm_research_max_concurrency=1,
        llm_research_backoff_max_seconds=5.0,
        llm_research_honor_retry_after=True,
    )
    success_body = {
        "provider": "foundry",
        "model": "gpt-4o",
        "content": (
            '{"findings": ["gateway timeout recovered"], "confidence": 0.8, "citations": []}'
        ),
        "citations": None,
        "usage": None,
        "grounding": None,
        "error": None,
    }
    attempt = {"count": 0}

    def side_effect(request):
        attempt["count"] += 1
        if attempt["count"] <= 2:
            return httpx.Response(504, json={"error": "gateway timeout"})
        return httpx.Response(200, json=success_body)

    with respx.mock(base_url=MODEL_ROUTER_URL) as mock:
        route = mock.post("/complete").mock(side_effect=side_effect)
        adapter = LlmResearchAdapter(settings)

        result = await adapter.research(CANDIDATE_DESCRIPTION, SOURCE_TEXT)

    assert result.findings == ["gateway timeout recovered"]
    assert route.call_count == 3


@pytest.mark.asyncio
async def test_backoff_capped_by_max_seconds():
    settings = EvidenceSettings(
        model_router_url=MODEL_ROUTER_URL,
        vector_router_url=VECTOR_ROUTER_URL,
        llm_research_timeout_seconds=10.0,
        llm_research_max_retries=3,
        llm_research_max_concurrency=1,
        llm_research_backoff_max_seconds=1.0,
        llm_research_honor_retry_after=True,
    )

    def side_effect(request):
        return httpx.Response(429, headers={"Retry-After": "999"}, json={"error": "rate limit"})

    with respx.mock(base_url=MODEL_ROUTER_URL) as mock:
        route = mock.post("/complete").mock(side_effect=side_effect)
        adapter = LlmResearchAdapter(settings)

        with pytest.raises(LlmResearchError):
            await adapter.research(CANDIDATE_DESCRIPTION, SOURCE_TEXT)

        assert route.call_count == 3


@pytest.mark.asyncio
async def test_retry_exhausted_raises_llm_research_error():
    settings = EvidenceSettings(
        model_router_url=MODEL_ROUTER_URL,
        vector_router_url=VECTOR_ROUTER_URL,
        llm_research_timeout_seconds=10.0,
        llm_research_max_retries=2,
        llm_research_max_concurrency=1,
        llm_research_backoff_max_seconds=1.0,
        llm_research_honor_retry_after=True,
    )

    def side_effect(request):
        return httpx.Response(429, json={"error": "persistent rate limit"})

    with respx.mock(base_url=MODEL_ROUTER_URL) as mock:
        route = mock.post("/complete").mock(side_effect=side_effect)
        adapter = LlmResearchAdapter(settings)

        with pytest.raises(LlmResearchError, match="LLM research request failed"):
            await adapter.research(CANDIDATE_DESCRIPTION, SOURCE_TEXT)

        assert route.call_count == 2


@pytest.mark.asyncio
async def test_retry_after_missing_falls_back_to_exponential():
    settings = EvidenceSettings(
        model_router_url=MODEL_ROUTER_URL,
        vector_router_url=VECTOR_ROUTER_URL,
        llm_research_timeout_seconds=10.0,
        llm_research_max_retries=3,
        llm_research_max_concurrency=1,
        llm_research_backoff_max_seconds=10.0,
        llm_research_honor_retry_after=True,
    )
    success_body = {
        "provider": "foundry",
        "model": "gpt-4o",
        "content": '{"findings": ["fallback worked"], "confidence": 0.75, "citations": []}',
        "citations": None,
        "usage": None,
        "grounding": None,
        "error": None,
    }
    attempt = {"count": 0}

    def side_effect(request):
        attempt["count"] += 1
        if attempt["count"] == 1:
            return httpx.Response(503, json={"error": "unavailable"})
        return httpx.Response(200, json=success_body)

    with respx.mock(base_url=MODEL_ROUTER_URL) as mock:
        route = mock.post("/complete").mock(side_effect=side_effect)
        adapter = LlmResearchAdapter(settings)

        result = await adapter.research(CANDIDATE_DESCRIPTION, SOURCE_TEXT)

    assert result.findings == ["fallback worked"]
    assert route.call_count == 2


@pytest.mark.asyncio
async def test_honor_retry_after_disabled_uses_exponential():
    settings = EvidenceSettings(
        model_router_url=MODEL_ROUTER_URL,
        vector_router_url=VECTOR_ROUTER_URL,
        llm_research_timeout_seconds=10.0,
        llm_research_max_retries=3,
        llm_research_max_concurrency=1,
        llm_research_backoff_max_seconds=5.0,
        llm_research_honor_retry_after=False,
    )
    success_body = {
        "provider": "foundry",
        "model": "gpt-4o",
        "content": '{"findings": ["no header honored"], "confidence": 0.7, "citations": []}',
        "citations": None,
        "usage": None,
        "grounding": None,
        "error": None,
    }
    attempt = {"count": 0}

    def side_effect(request):
        attempt["count"] += 1
        if attempt["count"] == 1:
            return httpx.Response(429, headers={"Retry-After": "100"}, json={"error": "rate limit"})
        return httpx.Response(200, json=success_body)

    with respx.mock(base_url=MODEL_ROUTER_URL) as mock:
        route = mock.post("/complete").mock(side_effect=side_effect)
        adapter = LlmResearchAdapter(settings)

        result = await adapter.research(CANDIDATE_DESCRIPTION, SOURCE_TEXT)

    assert result.findings == ["no header honored"]
    assert route.call_count == 2


@pytest.mark.asyncio
async def test_total_retry_budget_stops_retry_loop():
    settings = EvidenceSettings(
        model_router_url=MODEL_ROUTER_URL,
        vector_router_url=VECTOR_ROUTER_URL,
        llm_research_timeout_seconds=10.0,
        llm_research_max_retries=100,
        llm_research_total_retry_budget_seconds=2.0,
        llm_research_max_concurrency=1,
        llm_research_backoff_max_seconds=1.0,
        llm_research_honor_retry_after=True,
    )

    def side_effect(request):
        return httpx.Response(503, json={"error": "service unavailable"})

    with respx.mock(base_url=MODEL_ROUTER_URL) as mock:
        route = mock.post("/complete").mock(side_effect=side_effect)
        adapter = LlmResearchAdapter(settings)

        with pytest.raises(LlmResearchError, match="LLM research request failed"):
            await adapter.research(CANDIDATE_DESCRIPTION, SOURCE_TEXT)

        assert route.call_count < 100
        assert route.call_count >= 2


@pytest.mark.asyncio
async def test_total_budget_enforced_before_client_timeout():
    settings = EvidenceSettings(
        model_router_url=MODEL_ROUTER_URL,
        vector_router_url=VECTOR_ROUTER_URL,
        llm_research_timeout_seconds=150.0,
        llm_research_max_retries=10,
        llm_research_total_retry_budget_seconds=3.0,
        llm_research_max_concurrency=1,
        llm_research_backoff_max_seconds=1.5,
        llm_research_honor_retry_after=False,
    )
    import time

    start = time.time()

    def slow_side_effect(request):
        return httpx.Response(503, json={"error": "slow unavailable"})

    with respx.mock(base_url=MODEL_ROUTER_URL) as mock:
        route = mock.post("/complete").mock(side_effect=slow_side_effect)
        adapter = LlmResearchAdapter(settings)

        with pytest.raises(LlmResearchError):
            await adapter.research(CANDIDATE_DESCRIPTION, SOURCE_TEXT)

    elapsed = time.time() - start
    assert elapsed < 10.0
    assert route.call_count >= 2


@pytest.mark.asyncio
async def test_timeout_exception_retried_before_degrading():
    settings = EvidenceSettings(
        model_router_url=MODEL_ROUTER_URL,
        vector_router_url=VECTOR_ROUTER_URL,
        llm_research_timeout_seconds=10.0,
        llm_research_max_retries=3,
        llm_research_max_concurrency=1,
        llm_research_backoff_max_seconds=1.0,
        llm_research_honor_retry_after=True,
    )
    success_body = {
        "provider": "foundry",
        "model": "gpt-4o",
        "content": '{"findings": ["timeout recovered"], "confidence": 0.88, "citations": []}',
        "citations": None,
        "usage": None,
        "grounding": None,
        "error": None,
    }
    attempt = {"count": 0}

    def side_effect(request):
        attempt["count"] += 1
        if attempt["count"] <= 2:
            raise httpx.TimeoutException("request timed out")
        return httpx.Response(200, json=success_body)

    with respx.mock(base_url=MODEL_ROUTER_URL) as mock:
        route = mock.post("/complete").mock(side_effect=side_effect)
        adapter = LlmResearchAdapter(settings)

        result = await adapter.research(CANDIDATE_DESCRIPTION, SOURCE_TEXT)

    assert result.findings == ["timeout recovered"]
    assert result.confidence == pytest.approx(0.88)
    assert route.call_count == 3


@pytest.mark.asyncio
async def test_timeout_exception_error_message_includes_exception_type():
    settings = EvidenceSettings(
        model_router_url=MODEL_ROUTER_URL,
        vector_router_url=VECTOR_ROUTER_URL,
        llm_research_timeout_seconds=0.1,
        llm_research_max_retries=1,
        llm_research_max_concurrency=1,
    )
    with respx.mock(base_url=MODEL_ROUTER_URL) as mock:
        mock.post("/complete").mock(side_effect=httpx.TimeoutException(""))
        adapter = LlmResearchAdapter(settings)

        with pytest.raises(
            LlmResearchError, match=r"LLM research request failed: TimeoutException"
        ):
            await adapter.research(CANDIDATE_DESCRIPTION, SOURCE_TEXT)


@pytest.mark.asyncio
async def test_network_error_retried_before_degrading():
    settings = EvidenceSettings(
        model_router_url=MODEL_ROUTER_URL,
        vector_router_url=VECTOR_ROUTER_URL,
        llm_research_timeout_seconds=10.0,
        llm_research_max_retries=3,
        llm_research_max_concurrency=1,
        llm_research_backoff_max_seconds=1.0,
        llm_research_honor_retry_after=True,
    )
    success_body = {
        "provider": "foundry",
        "model": "gpt-4o",
        "content": (
            '{"findings": ["network recovered"], "confidence": 0.91, "citations": ["US999"]}'
        ),
        "citations": None,
        "usage": None,
        "grounding": None,
        "error": None,
    }
    attempt = {"count": 0}

    def side_effect(request):
        attempt["count"] += 1
        if attempt["count"] == 1:
            raise httpx.NetworkError("network failure")
        return httpx.Response(200, json=success_body)

    with respx.mock(base_url=MODEL_ROUTER_URL) as mock:
        route = mock.post("/complete").mock(side_effect=side_effect)
        adapter = LlmResearchAdapter(settings)

        result = await adapter.research(CANDIDATE_DESCRIPTION, SOURCE_TEXT)

    assert result.findings == ["network recovered"]
    assert result.confidence == pytest.approx(0.91)
    assert route.call_count == 2


@pytest.mark.asyncio
async def test_retry_budget_exhausted_raises_with_non_empty_message():
    settings = EvidenceSettings(
        model_router_url=MODEL_ROUTER_URL,
        vector_router_url=VECTOR_ROUTER_URL,
        llm_research_timeout_seconds=10.0,
        llm_research_max_retries=2,
        llm_research_total_retry_budget_seconds=2.0,
        llm_research_max_concurrency=1,
        llm_research_backoff_max_seconds=1.0,
        llm_research_honor_retry_after=True,
    )

    def side_effect(request):
        raise httpx.TimeoutException("")

    with respx.mock(base_url=MODEL_ROUTER_URL) as mock:
        route = mock.post("/complete").mock(side_effect=side_effect)
        adapter = LlmResearchAdapter(settings)

        with pytest.raises(
            LlmResearchError, match=r"LLM research request failed: TimeoutException"
        ):
            await adapter.research(CANDIDATE_DESCRIPTION, SOURCE_TEXT)

        assert route.call_count == 2


_SKIP_TEST_SUCCESS_BODY = {
    "provider": "foundry",
    "model": "gpt-4o",
    "content": '{"findings": ["result"], "confidence": 0.8, "citations": ["US1"]}',
    "citations": None,
    "usage": None,
    "grounding": None,
    "error": None,
}


@pytest.mark.asyncio
async def test_research_both_none_raises_without_http_call(llm_research_settings):
    with respx.mock(base_url=MODEL_ROUTER_URL, assert_all_called=False) as mock:
        route = mock.post("/complete").mock(
            return_value=httpx.Response(200, json=_SKIP_TEST_SUCCESS_BODY)
        )
        adapter = LlmResearchAdapter(llm_research_settings)

        with pytest.raises(LlmResearchError) as exc_info:
            await adapter.research(None, None)

    assert exc_info.value.status_code is None
    assert route.call_count == 0


@pytest.mark.asyncio
async def test_research_both_empty_string_raises_without_http_call(llm_research_settings):
    with respx.mock(base_url=MODEL_ROUTER_URL, assert_all_called=False) as mock:
        route = mock.post("/complete").mock(
            return_value=httpx.Response(200, json=_SKIP_TEST_SUCCESS_BODY)
        )
        adapter = LlmResearchAdapter(llm_research_settings)

        with pytest.raises(LlmResearchError) as exc_info:
            await adapter.research("", "")

    assert exc_info.value.status_code is None
    assert route.call_count == 0


@pytest.mark.asyncio
async def test_research_both_whitespace_raises_without_http_call(llm_research_settings):
    with respx.mock(base_url=MODEL_ROUTER_URL, assert_all_called=False) as mock:
        route = mock.post("/complete").mock(
            return_value=httpx.Response(200, json=_SKIP_TEST_SUCCESS_BODY)
        )
        adapter = LlmResearchAdapter(llm_research_settings)

        with pytest.raises(LlmResearchError) as exc_info:
            await adapter.research("   ", "  ")

    assert exc_info.value.status_code is None
    assert route.call_count == 0


@pytest.mark.asyncio
async def test_research_empty_desc_nonempty_source_proceeds(llm_research_settings):
    with respx.mock(base_url=MODEL_ROUTER_URL) as mock:
        route = mock.post("/complete").mock(
            return_value=httpx.Response(200, json=_SKIP_TEST_SUCCESS_BODY)
        )
        adapter = LlmResearchAdapter(llm_research_settings)

        result = await adapter.research("", "some source text")

    assert result.findings == ["result"]
    assert route.call_count == 1


@pytest.mark.asyncio
async def test_research_none_desc_nonempty_source_proceeds(llm_research_settings):
    with respx.mock(base_url=MODEL_ROUTER_URL) as mock:
        route = mock.post("/complete").mock(
            return_value=httpx.Response(200, json=_SKIP_TEST_SUCCESS_BODY)
        )
        adapter = LlmResearchAdapter(llm_research_settings)

        result = await adapter.research(None, "some source text")

    assert result.findings == ["result"]
    assert route.call_count == 1


def test_prompt_builder_coerces_none_to_empty_string():
    prompt = build_research_prompt(None, None)

    assert "None" not in prompt
    assert len(prompt) > 0


@pytest.mark.asyncio
async def test_retry_budget_covers_max_attempts_with_backoff():
    settings = EvidenceSettings(
        model_router_url=MODEL_ROUTER_URL,
        vector_router_url=VECTOR_ROUTER_URL,
        llm_research_timeout_seconds=170.0,
        llm_research_max_retries=2,
        llm_research_total_retry_budget_seconds=400.0,
        llm_research_max_concurrency=1,
        llm_research_backoff_max_seconds=10.0,
        llm_research_honor_retry_after=True,
    )
    attempt = {"count": 0}

    def side_effect(request):
        attempt["count"] += 1
        raise httpx.TimeoutException("simulated timeout")

    with respx.mock(base_url=MODEL_ROUTER_URL) as mock:
        route = mock.post("/complete").mock(side_effect=side_effect)
        adapter = LlmResearchAdapter(settings)

        with pytest.raises(LlmResearchError):
            await adapter.research(CANDIDATE_DESCRIPTION, SOURCE_TEXT)

        assert route.call_count == 2
