import httpx
import pytest
import respx

from extraction.application.dtos.model_complete_result import (
    DualModelCompleteResult,
    ModelCompleteResult,
    TokenUsage,
)
from extraction.domain.enums.model_mode import ModelMode
from extraction.domain.errors.extraction_errors import ModelCallFailed
from extraction.infrastructure.model_router.model_router_client import (
    ModelRouterClient,
    RetryConfig,
    _build_payload,
)

BASE_URL = "http://model-router-test"

# Mirrors the model-router (.NET) wire shape: ASP.NET default camelCase serialization,
# so the nested usage object uses promptTokens/completionTokens/totalTokens.
SINGLE_RESPONSE = {
    "Provider": "azure-openai",
    "Model": "gpt-4o",
    "Content": '{"candidates": []}',
    "Citations": None,
    "Usage": {"promptTokens": 100, "completionTokens": 50, "totalTokens": 150},
    "Grounding": None,
    "Error": None,
}

DUAL_RESPONSE = {
    "Primary": SINGLE_RESPONSE,
    "Secondary": {**SINGLE_RESPONSE, "Provider": "anthropic", "Model": "claude-3-5-sonnet"},
}


def make_client(
    http_client: httpx.AsyncClient,
    max_retries: int = 1,
    backoff_max: float = 10.0,
    honor_retry_after: bool = True,
    total_budget: float = 30.0,
) -> ModelRouterClient:
    retry_config = RetryConfig(
        max_retries=max_retries,
        backoff_max=backoff_max,
        honor_retry_after=honor_retry_after,
        total_budget=total_budget,
    )
    return ModelRouterClient.with_retries(
        http_client=http_client,
        base_url=BASE_URL,
        timeout=5.0,
        retry_config=retry_config,
    )


class TestPayloadOptions:
    def test_build_payload_includes_force_json_output(self, sample_prompt):
        payload = _build_payload(sample_prompt, ModelMode.single_primary)
        assert payload["options"]["force_json_output"] is True


class TestSingleMode:
    async def test_single_primary_hits_complete_endpoint(self, sample_prompt):
        with respx.mock(base_url=BASE_URL) as mock:
            route = mock.post("/complete").mock(
                return_value=httpx.Response(200, json=SINGLE_RESPONSE)
            )
            async with httpx.AsyncClient() as http:
                client = make_client(http)
                result = await client.call(sample_prompt, ModelMode.single_primary)

        assert route.called
        assert isinstance(result, ModelCompleteResult)

    async def test_single_secondary_hits_complete_endpoint(self, sample_prompt):
        with respx.mock(base_url=BASE_URL) as mock:
            route = mock.post("/complete").mock(
                return_value=httpx.Response(200, json=SINGLE_RESPONSE)
            )
            async with httpx.AsyncClient() as http:
                client = make_client(http)
                result = await client.call(sample_prompt, ModelMode.single_secondary)

        assert route.called
        assert isinstance(result, ModelCompleteResult)

    async def test_result_dto_mapping(self, sample_prompt):
        with respx.mock(base_url=BASE_URL) as mock:
            mock.post("/complete").mock(return_value=httpx.Response(200, json=SINGLE_RESPONSE))
            async with httpx.AsyncClient() as http:
                client = make_client(http)
                result = await client.complete(sample_prompt, ModelMode.single_primary)

        assert result.provider == "azure-openai"
        assert result.model == "gpt-4o"
        assert result.usage is not None
        assert result.usage.total_tokens == 150
        assert result.usage.prompt_tokens == 100
        assert result.usage.completion_tokens == 50


class TestTokenUsageAliases:
    def test_parses_camel_case_usage(self):
        usage = TokenUsage.model_validate(
            {"promptTokens": 12, "completionTokens": 5, "totalTokens": 17}
        )
        assert (usage.prompt_tokens, usage.completion_tokens, usage.total_tokens) == (12, 5, 17)

    def test_parses_snake_case_usage(self):
        usage = TokenUsage.model_validate(
            {"prompt_tokens": 12, "completion_tokens": 5, "total_tokens": 17}
        )
        assert (usage.prompt_tokens, usage.completion_tokens, usage.total_tokens) == (12, 5, 17)


class TestDualMode:
    async def test_dual_adversarial_hits_complete_dual_endpoint(self, sample_prompt):
        with respx.mock(base_url=BASE_URL) as mock:
            route = mock.post("/complete/dual").mock(
                return_value=httpx.Response(200, json=DUAL_RESPONSE)
            )
            async with httpx.AsyncClient() as http:
                client = make_client(http)
                result = await client.call(sample_prompt, ModelMode.dual_adversarial)

        assert route.called
        assert isinstance(result, DualModelCompleteResult)
        assert result.primary.provider == "azure-openai"
        assert result.secondary.provider == "anthropic"

    async def test_dual_does_not_hit_single_endpoint(self, sample_prompt):
        with respx.mock(base_url=BASE_URL, assert_all_called=False) as mock:
            mock.post("/complete/dual").mock(return_value=httpx.Response(200, json=DUAL_RESPONSE))
            single_route = mock.post("/complete").mock(
                return_value=httpx.Response(200, json=SINGLE_RESPONSE)
            )
            async with httpx.AsyncClient() as http:
                client = make_client(http)
                await client.call(sample_prompt, ModelMode.dual_adversarial)

        assert not single_route.called


class TestAiModelOverride:
    async def test_call_includes_model_key_when_ai_model_provided(self, sample_prompt):
        with respx.mock(base_url=BASE_URL) as mock:
            route = mock.post("/complete").mock(
                return_value=httpx.Response(200, json=SINGLE_RESPONSE)
            )
            async with httpx.AsyncClient() as http:
                client = make_client(http)
                await client.call(sample_prompt, ModelMode.single_primary, ai_model="gpt-5.4")

        assert route.called
        sent_body = route.calls.last.request.content
        assert b'"model":"gpt-5.4"' in sent_body

    async def test_call_omits_model_key_when_ai_model_absent(self, sample_prompt):
        with respx.mock(base_url=BASE_URL) as mock:
            route = mock.post("/complete").mock(
                return_value=httpx.Response(200, json=SINGLE_RESPONSE)
            )
            async with httpx.AsyncClient() as http:
                client = make_client(http)
                await client.call(sample_prompt, ModelMode.single_primary)

        assert route.called
        sent_body = route.calls.last.request.content
        assert b'"model"' not in sent_body

    async def test_complete_dual_forwards_ai_model_to_both_calls(self, sample_prompt):
        with respx.mock(base_url=BASE_URL) as mock:
            route = mock.post("/complete/dual").mock(
                return_value=httpx.Response(200, json=DUAL_RESPONSE)
            )
            async with httpx.AsyncClient() as http:
                client = make_client(http)
                await client.call(sample_prompt, ModelMode.dual_adversarial, ai_model="gpt-5.4")

        assert route.called
        sent_body = route.calls.last.request.content
        assert b'"model":"gpt-5.4"' in sent_body


class TestRetryBehavior:
    async def test_retries_on_timeout_then_succeeds(self, sample_prompt):
        call_count = 0

        def side_effect(request):
            nonlocal call_count
            call_count += 1
            if call_count < 2:
                raise httpx.ReadTimeout("timeout", request=request)
            return httpx.Response(200, json=SINGLE_RESPONSE)

        with respx.mock(base_url=BASE_URL) as mock:
            mock.post("/complete").mock(side_effect=side_effect)
            async with httpx.AsyncClient() as http:
                client = make_client(http, max_retries=3)
                result = await client.complete(sample_prompt, ModelMode.single_primary)

        assert call_count == 2
        assert isinstance(result, ModelCompleteResult)

    async def test_retries_on_502(self, sample_prompt):
        call_count = 0

        def side_effect(request):
            nonlocal call_count
            call_count += 1
            if call_count < 2:
                return httpx.Response(502)
            return httpx.Response(200, json=SINGLE_RESPONSE)

        with respx.mock(base_url=BASE_URL) as mock:
            mock.post("/complete").mock(side_effect=side_effect)
            async with httpx.AsyncClient() as http:
                client = make_client(http, max_retries=3)
                result = await client.complete(sample_prompt, ModelMode.single_primary)

        assert call_count == 2
        assert isinstance(result, ModelCompleteResult)

    async def test_exhausted_retries_raises_model_call_failed(self, sample_prompt):
        with respx.mock(base_url=BASE_URL) as mock:
            mock.post("/complete").mock(return_value=httpx.Response(503))
            async with httpx.AsyncClient() as http:
                client = make_client(http, max_retries=2)
                with pytest.raises(ModelCallFailed):
                    await client.complete(sample_prompt, ModelMode.single_primary)

    async def test_does_not_retry_on_4xx(self, sample_prompt):
        call_count = 0

        def side_effect(request):
            nonlocal call_count
            call_count += 1
            return httpx.Response(422)

        with respx.mock(base_url=BASE_URL) as mock:
            mock.post("/complete").mock(side_effect=side_effect)
            async with httpx.AsyncClient() as http:
                client = make_client(http, max_retries=3)
                with pytest.raises(ModelCallFailed) as exc_info:
                    await client.complete(sample_prompt, ModelMode.single_primary)

        assert call_count == 1
        assert exc_info.value.status_code == 422

    async def test_honors_retry_after_on_429(self, sample_prompt):
        call_count = 0

        def side_effect(request):
            nonlocal call_count
            call_count += 1
            if call_count == 1:
                return httpx.Response(429, headers={"Retry-After": "1"})
            return httpx.Response(200, json=SINGLE_RESPONSE)

        with respx.mock(base_url=BASE_URL) as mock:
            mock.post("/complete").mock(side_effect=side_effect)
            async with httpx.AsyncClient() as http:
                client = make_client(http, max_retries=3, honor_retry_after=True)
                result = await client.complete(sample_prompt, ModelMode.single_primary)

        assert call_count == 2
        assert isinstance(result, ModelCompleteResult)

    async def test_caps_retry_after_at_backoff_max(self, sample_prompt):
        call_count = 0

        def side_effect(request):
            nonlocal call_count
            call_count += 1
            if call_count == 1:
                return httpx.Response(503, headers={"Retry-After": "999"})
            return httpx.Response(200, json=SINGLE_RESPONSE)

        with respx.mock(base_url=BASE_URL) as mock:
            mock.post("/complete").mock(side_effect=side_effect)
            async with httpx.AsyncClient() as http:
                client = make_client(http, max_retries=3, backoff_max=2.0)
                result = await client.complete(sample_prompt, ModelMode.single_primary)

        assert call_count == 2
        assert isinstance(result, ModelCompleteResult)

    async def test_total_budget_stops_before_max_retries(self, sample_prompt):
        def side_effect(request):
            return httpx.Response(503)

        with respx.mock(base_url=BASE_URL) as mock:
            mock.post("/complete").mock(side_effect=side_effect)
            async with httpx.AsyncClient() as http:
                client = make_client(http, max_retries=10, total_budget=2.0)
                with pytest.raises(ModelCallFailed):
                    await client.complete(sample_prompt, ModelMode.single_primary)

    async def test_exhausted_budget_raises_model_call_failed(self, sample_prompt):
        call_count = 0

        def side_effect(request):
            nonlocal call_count
            call_count += 1
            return httpx.Response(429)

        with respx.mock(base_url=BASE_URL) as mock:
            mock.post("/complete").mock(side_effect=side_effect)
            async with httpx.AsyncClient() as http:
                client = make_client(http, max_retries=5, total_budget=5.0)
                with pytest.raises(ModelCallFailed):
                    await client.complete(sample_prompt, ModelMode.single_primary)

        assert call_count >= 2


class TestContentFilterErrorCode:
    async def test_400_with_content_filter_code_extracts_error_code_and_categories(
        self, sample_prompt
    ):
        problem_details_body = {
            "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
            "title": "Content filter",
            "status": 400,
            "detail": "The prompt was rejected by the content filter.",
            "code": "content_filter",
            "categories": ["jailbreak", "violence"],
        }
        with respx.mock(base_url=BASE_URL) as mock:
            mock.post("/complete").mock(return_value=httpx.Response(400, json=problem_details_body))
            async with httpx.AsyncClient() as http:
                client = make_client(http)
                with pytest.raises(ModelCallFailed) as exc_info:
                    await client.complete(sample_prompt, ModelMode.single_primary)

        assert exc_info.value.status_code == 400
        assert exc_info.value.error_code == "content_filter"
        assert exc_info.value.categories == ["jailbreak", "violence"]

    async def test_400_without_content_filter_code_has_none_error_code(self, sample_prompt):
        problem_details_body = {
            "title": "Invalid request",
            "status": 400,
            "code": "invalid_request",
        }
        with respx.mock(base_url=BASE_URL) as mock:
            mock.post("/complete").mock(return_value=httpx.Response(400, json=problem_details_body))
            async with httpx.AsyncClient() as http:
                client = make_client(http)
                with pytest.raises(ModelCallFailed) as exc_info:
                    await client.complete(sample_prompt, ModelMode.single_primary)

        assert exc_info.value.status_code == 400
        assert exc_info.value.error_code == "invalid_request"
        assert exc_info.value.categories is None

    async def test_400_with_non_json_body_has_none_error_code(self, sample_prompt):
        with respx.mock(base_url=BASE_URL) as mock:
            mock.post("/complete").mock(return_value=httpx.Response(400, text="Bad Request"))
            async with httpx.AsyncClient() as http:
                client = make_client(http)
                with pytest.raises(ModelCallFailed) as exc_info:
                    await client.complete(sample_prompt, ModelMode.single_primary)

        assert exc_info.value.status_code == 400
        assert exc_info.value.error_code is None
        assert exc_info.value.categories is None

    async def test_400_with_empty_body_has_none_error_code(self, sample_prompt):
        with respx.mock(base_url=BASE_URL) as mock:
            mock.post("/complete").mock(return_value=httpx.Response(400))
            async with httpx.AsyncClient() as http:
                client = make_client(http)
                with pytest.raises(ModelCallFailed) as exc_info:
                    await client.complete(sample_prompt, ModelMode.single_primary)

        assert exc_info.value.status_code == 400
        assert exc_info.value.error_code is None
        assert exc_info.value.categories is None

    async def test_400_with_nested_extensions_fallback(self, sample_prompt):
        problem_details_body = {
            "title": "Content filter",
            "status": 400,
            "extensions": {"code": "content_filter", "categories": ["hate"]},
        }
        with respx.mock(base_url=BASE_URL) as mock:
            mock.post("/complete").mock(return_value=httpx.Response(400, json=problem_details_body))
            async with httpx.AsyncClient() as http:
                client = make_client(http)
                with pytest.raises(ModelCallFailed) as exc_info:
                    await client.complete(sample_prompt, ModelMode.single_primary)

        assert exc_info.value.status_code == 400
        assert exc_info.value.error_code == "content_filter"
        assert exc_info.value.categories == ["hate"]
