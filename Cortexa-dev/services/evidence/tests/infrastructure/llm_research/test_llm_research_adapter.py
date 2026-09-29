import httpx
import pytest
import respx

from evidence.domain.errors.evidence_errors import LlmResearchError
from evidence.domain.models.research_finding import ResearchFinding
from evidence.infrastructure.config.settings import EvidenceSettings
from evidence.infrastructure.llm_research.llm_research_adapter import (
    LlmResearchAdapter,
    _parse_content,
)

_MODEL_ROUTER_URL = "http://test-model-router"

_PRIMARY_MODEL = "gpt-5.5"

_SINGLE_RESPONSE_PRIMARY = {
    "content": '{"findings": ["primary finding"], "confidence": 0.8, "citations": ["US1111111"]}',
    "error": None,
}


def _make_settings() -> EvidenceSettings:
    return EvidenceSettings(
        model_router_url=_MODEL_ROUTER_URL,
        vector_router_url="http://test-vector-router",
        llm_research_timeout_seconds=30,
        llm_research_max_concurrency=2,
        llm_research_max_retries=3,
        llm_research_backoff_max_seconds=10,
        llm_research_honor_retry_after=True,
        llm_research_total_retry_budget_seconds=60,
    )


@pytest.mark.asyncio
async def test_research_single_shot_issues_one_complete_call():
    with respx.mock(base_url=_MODEL_ROUTER_URL) as respx_mock:
        route = respx_mock.post("/complete").mock(
            return_value=httpx.Response(200, json=_SINGLE_RESPONSE_PRIMARY)
        )

        settings = _make_settings()
        adapter = LlmResearchAdapter(settings)

        result = await adapter.research(
            candidate_description="test claim",
            source_text="test field",
            model=_PRIMARY_MODEL,
        )

        assert route.call_count == 1
        assert isinstance(result, ResearchFinding)
        assert "primary finding" in result.findings


@pytest.mark.asyncio
async def test_research_single_mode_no_model_issues_one_complete_call():
    with respx.mock(base_url=_MODEL_ROUTER_URL) as respx_mock:
        route = respx_mock.post("/complete").mock(
            return_value=httpx.Response(200, json=_SINGLE_RESPONSE_PRIMARY)
        )

        settings = _make_settings()
        adapter = LlmResearchAdapter(settings)

        result = await adapter.research(
            candidate_description="test claim",
            source_text="test field",
        )

        assert route.call_count == 1
        assert isinstance(result, ResearchFinding)


@pytest.mark.asyncio
async def test_research_body_contains_stamped_model():
    import json

    STAMPED_MODEL = "gpt-5.4"

    with respx.mock(base_url=_MODEL_ROUTER_URL) as respx_mock:
        route = respx_mock.post("/complete").mock(
            return_value=httpx.Response(200, json=_SINGLE_RESPONSE_PRIMARY)
        )

        settings = _make_settings()
        adapter = LlmResearchAdapter(settings)

        await adapter.research(
            candidate_description="test claim",
            source_text="test field",
            model=STAMPED_MODEL,
        )

        assert route.call_count == 1
        call = route.calls[0]
        body = json.loads(call.request.content)
        assert body["model"] == STAMPED_MODEL


@pytest.mark.asyncio
async def test_research_primary_4xx_raises_llm_research_error_with_status_code():
    ERROR_STATUS_CODE = 400

    with respx.mock(base_url=_MODEL_ROUTER_URL) as respx_mock:
        respx_mock.post("/complete").mock(
            return_value=httpx.Response(ERROR_STATUS_CODE, json={"error": "Bad request"})
        )

        settings = _make_settings()
        adapter = LlmResearchAdapter(settings)

        with pytest.raises(LlmResearchError) as exc_info:
            await adapter.research(
                candidate_description="test claim",
                source_text="test field",
                model=_PRIMARY_MODEL,
            )

        assert exc_info.value.status_code == ERROR_STATUS_CODE


@pytest.mark.asyncio
async def test_research_transient_failure_raises_llm_research_error():
    with respx.mock(base_url=_MODEL_ROUTER_URL) as respx_mock:
        route = respx_mock.post("/complete").mock(
            side_effect=httpx.ConnectError("connection reset")
        )

        settings = EvidenceSettings(
            model_router_url=_MODEL_ROUTER_URL,
            vector_router_url="http://test-vector-router",
            llm_research_timeout_seconds=30,
            llm_research_max_concurrency=2,
            llm_research_max_retries=0,
        )
        adapter = LlmResearchAdapter(settings)

        with pytest.raises(LlmResearchError):
            await adapter.research(
                candidate_description="test claim",
                source_text="test field",
                model=_PRIMARY_MODEL,
            )

        assert route.call_count == 1


def test_parse_content_valid_json_returns_research_finding():
    CONTENT = '{"findings": ["test"], "confidence": 0.9, "citations": ["US1234567"]}'

    result = _parse_content(CONTENT)

    assert isinstance(result, ResearchFinding)
    assert result.findings == ["test"]
    assert result.confidence == 0.9
    assert [c.id for c in result.citations] == ["US1234567"]


def test_parse_content_per_citation_confidence_preserved():
    CONTENT = (
        '{"findings": ["test"], "confidence": 0.7, "citations": '
        '[{"id": "US1234567", "confidence": 0.42}, {"id": "EP9876543", "confidence": 0.91}]}'
    )

    result = _parse_content(CONTENT)

    assert [(c.id, c.confidence) for c in result.citations] == [
        ("US1234567", 0.42),
        ("EP9876543", 0.91),
    ]


def test_parse_content_per_citation_confidence_clamped():
    CONTENT = (
        '{"findings": ["test"], "confidence": 0.7, "citations": '
        '[{"id": "US1", "confidence": 1.8}, {"id": "US2", "confidence": -0.3}]}'
    )

    result = _parse_content(CONTENT)

    assert [c.confidence for c in result.citations] == [1.0, 0.0]


def test_parse_content_legacy_string_citations_fall_back_to_finding_confidence():
    CONTENT = '{"findings": ["test"], "confidence": 0.64, "citations": ["US1234567", "EP1"]}'

    result = _parse_content(CONTENT)

    assert [c.confidence for c in result.citations] == [0.64, 0.64]


def test_parse_content_citation_missing_confidence_falls_back():
    CONTENT = '{"findings": ["test"], "confidence": 0.55, "citations": [{"id": "US1234567"}]}'

    result = _parse_content(CONTENT)

    assert result.citations[0].confidence == 0.55


def test_parse_content_json_with_markdown_fence_strips_fence():
    CONTENT = '```json\n{"findings": ["test"], "confidence": 0.9, "citations": []}\n```'

    result = _parse_content(CONTENT)

    assert isinstance(result, ResearchFinding)
    assert result.findings == ["test"]
