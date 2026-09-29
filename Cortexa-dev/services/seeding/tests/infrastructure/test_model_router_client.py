import asyncio
from unittest.mock import AsyncMock, Mock

import httpx
import pytest

from seeding.infrastructure.clients.model_router_client import ModelRouterClient, RetryConfig


@pytest.fixture
def client():
    retry_config = RetryConfig(
        max_retries=4,
        backoff_max_seconds=5.0,
        honor_retry_after=True,
        total_retry_budget_seconds=15.0,
    )
    return ModelRouterClient(
        base_url="http://test",
        task_kind="test-task",
        timeout=10.0,
        retry_config=retry_config,
        max_output_tokens=16384,
    )


def _mock_ok_response(content: str, citations: list[str] | None):
    mock = Mock(spec=httpx.Response)
    mock.raise_for_status = Mock()
    mock.json.return_value = {
        "provider": "test-provider",
        "content": content,
        "citations": citations,
    }
    return mock


def _mock_foundry_response(content: str):
    mock = Mock(spec=httpx.Response)
    mock.raise_for_status = Mock()
    mock.json.return_value = {
        "provider": "Foundry",
        "model": "gpt-5.5",
        "content": content,
        "citations": None,
        "usage": {"promptTokens": 10, "completionTokens": 20, "totalTokens": 30},
        "grounding": None,
        "finishReason": "stop",
    }
    return mock


@pytest.mark.asyncio
async def test_complete_success_no_retry(client, monkeypatch):
    mock_response = _mock_ok_response("result", [])
    mock_post = AsyncMock(return_value=mock_response)
    monkeypatch.setattr(client._client, "post", mock_post)

    result = await client.complete(prompt="test", evidence_refs=["ref1"], model="gpt")

    assert result.content == "result"
    assert result.citations == []
    assert mock_post.call_count == 1


@pytest.mark.asyncio
async def test_complete_accepts_realistic_foundry_response(client, monkeypatch):
    expected_content = "generated seeding narrative"
    mock_response = _mock_foundry_response(expected_content)
    mock_post = AsyncMock(return_value=mock_response)
    monkeypatch.setattr(client._client, "post", mock_post)

    result = await client.complete(prompt="test", evidence_refs=["ref1"], model="gpt-5.5")

    assert result.content == expected_content
    assert result.citations == []
    assert mock_post.call_count == 1


@pytest.mark.asyncio
async def test_complete_surfaces_camelcase_usage_and_model(client, monkeypatch):
    mock_response = _mock_foundry_response("narrative")
    monkeypatch.setattr(client._client, "post", AsyncMock(return_value=mock_response))

    result = await client.complete(prompt="test", evidence_refs=[], model="gpt-5.5")

    assert result.model == "gpt-5.5"
    assert result.provider == "Foundry"
    assert result.prompt_tokens == 10
    assert result.completion_tokens == 20
    assert result.total_tokens == 30
    assert result.finish_reason == "stop"


@pytest.mark.asyncio
async def test_complete_null_usage_yields_zero_token_fields(client, monkeypatch):
    mock_response = _mock_ok_response("result", [])
    monkeypatch.setattr(client._client, "post", AsyncMock(return_value=mock_response))

    result = await client.complete(prompt="test", evidence_refs=[], model=None)

    assert result.prompt_tokens == 0
    assert result.completion_tokens == 0
    assert result.total_tokens == 0
    assert result.model == ""
    assert result.finish_reason is None


@pytest.mark.asyncio
async def test_complete_429_with_retry_after_honored(client, monkeypatch):
    mock_response_429 = Mock(spec=httpx.Response)
    mock_response_429.status_code = 429
    mock_response_429.headers = {"Retry-After": "0.5"}
    mock_response_429.raise_for_status = Mock(
        side_effect=httpx.HTTPStatusError("429", request=Mock(), response=mock_response_429)
    )

    mock_response_ok = _mock_ok_response("success", [])

    mock_post = AsyncMock(side_effect=[mock_response_429, mock_response_ok])
    monkeypatch.setattr(client._client, "post", mock_post)

    result = await client.complete(prompt="test", evidence_refs=[], model=None)

    assert result.content == "success"
    assert mock_post.call_count == 2


@pytest.mark.asyncio
async def test_complete_503_retries_until_success(client, monkeypatch):
    mock_response_503 = Mock(spec=httpx.Response)
    mock_response_503.status_code = 503
    mock_response_503.headers = {}
    mock_response_503.raise_for_status = Mock(
        side_effect=httpx.HTTPStatusError("503", request=Mock(), response=mock_response_503)
    )

    mock_response_ok = _mock_ok_response("recovered", ["c1"])

    mock_post = AsyncMock(side_effect=[mock_response_503, mock_response_503, mock_response_ok])
    monkeypatch.setattr(client._client, "post", mock_post)

    result = await client.complete(prompt="test", evidence_refs=["ref"], model=None)

    assert result.content == "recovered"
    assert mock_post.call_count == 3


@pytest.mark.asyncio
async def test_complete_exhausts_max_retries(client, monkeypatch):
    from seeding.domain.errors.seeding_errors import ModelRouterFailedError

    mock_response_500 = Mock(spec=httpx.Response)
    mock_response_500.status_code = 500
    mock_response_500.headers = {}
    mock_response_500.text = "internal server error"
    mock_response_500.raise_for_status = Mock(
        side_effect=httpx.HTTPStatusError("500", request=Mock(), response=mock_response_500)
    )

    mock_post = AsyncMock(return_value=mock_response_500)
    monkeypatch.setattr(client._client, "post", mock_post)

    with pytest.raises(ModelRouterFailedError) as exc_info:
        await client.complete(prompt="test", evidence_refs=[], model=None)

    assert exc_info.value.status_code == 500
    assert mock_post.call_count == 4


@pytest.mark.asyncio
async def test_complete_respects_total_delay_budget(monkeypatch):
    from seeding.domain.errors.seeding_errors import ModelRouterFailedError

    retry_config = RetryConfig(
        max_retries=10,
        backoff_max_seconds=3.0,
        honor_retry_after=True,
        total_retry_budget_seconds=2.0,
    )
    client_short_budget = ModelRouterClient(
        base_url="http://test",
        task_kind="test",
        timeout=10.0,
        retry_config=retry_config,
        max_output_tokens=16384,
    )

    mock_response_429 = Mock(spec=httpx.Response)
    mock_response_429.status_code = 429
    mock_response_429.headers = {}
    mock_response_429.text = "rate limit exceeded"
    mock_response_429.raise_for_status = Mock(
        side_effect=httpx.HTTPStatusError("429", request=Mock(), response=mock_response_429)
    )

    mock_post = AsyncMock(return_value=mock_response_429)
    monkeypatch.setattr(client_short_budget._client, "post", mock_post)

    with pytest.raises(ModelRouterFailedError):
        await client_short_budget.complete(prompt="test", evidence_refs=[], model=None)

    assert mock_post.call_count < 10


@pytest.mark.asyncio
async def test_complete_400_permanent_no_retry(client, monkeypatch):
    from seeding.domain.errors.seeding_errors import ModelRouterFailedError

    mock_response_400 = Mock(spec=httpx.Response)
    mock_response_400.status_code = 400
    mock_response_400.headers = {}
    mock_response_400.text = '{"error":"content_filter"}'
    mock_response_400.raise_for_status = Mock(
        side_effect=httpx.HTTPStatusError("400", request=Mock(), response=mock_response_400)
    )

    mock_post = AsyncMock(return_value=mock_response_400)
    monkeypatch.setattr(client._client, "post", mock_post)

    with pytest.raises(ModelRouterFailedError) as exc_info:
        await client.complete(prompt="test", evidence_refs=[], model=None)

    assert exc_info.value.status_code == 400
    assert "400" in str(exc_info.value)
    assert mock_post.call_count == 1


@pytest.mark.asyncio
async def test_complete_cancelled_not_retried(client, monkeypatch):
    async def raise_cancelled(*args, **kwargs):
        raise asyncio.CancelledError()

    mock_post = AsyncMock(side_effect=raise_cancelled)
    monkeypatch.setattr(client._client, "post", mock_post)

    with pytest.raises(asyncio.CancelledError):
        await client.complete(prompt="test", evidence_refs=[], model=None)

    assert mock_post.call_count == 1


@pytest.mark.asyncio
async def test_complete_network_error_retries(client, monkeypatch):
    mock_ok = _mock_ok_response("ok", [])

    mock_post = AsyncMock(side_effect=[httpx.NetworkError("connection"), mock_ok])
    monkeypatch.setattr(client._client, "post", mock_post)

    result = await client.complete(prompt="test", evidence_refs=[], model=None)

    assert result.content == "ok"
    assert mock_post.call_count == 2


@pytest.mark.asyncio
async def test_complete_400_wrapped_with_status_code_and_text_preview(client, monkeypatch):
    from seeding.domain.errors.seeding_errors import ModelRouterFailedError

    error_body = '{"error":"content_filter","innererror":{"code":"ResponsibleAIPolicyViolation"}}'
    mock_response_400 = Mock(spec=httpx.Response)
    mock_response_400.status_code = 400
    mock_response_400.headers = {}
    mock_response_400.text = error_body
    mock_response_400.raise_for_status = Mock(
        side_effect=httpx.HTTPStatusError("400", request=Mock(), response=mock_response_400)
    )

    mock_post = AsyncMock(return_value=mock_response_400)
    monkeypatch.setattr(client._client, "post", mock_post)

    with pytest.raises(ModelRouterFailedError) as exc_info:
        await client.complete(prompt="test", evidence_refs=[], model=None)

    assert exc_info.value.status_code == 400
    assert "400" in str(exc_info.value)
    assert error_body[:200] in str(exc_info.value)
    assert mock_post.call_count == 1


@pytest.mark.asyncio
async def test_complete_5xx_still_retried_before_wrapping(client, monkeypatch):
    from seeding.domain.errors.seeding_errors import ModelRouterFailedError

    mock_response_502 = Mock(spec=httpx.Response)
    mock_response_502.status_code = 502
    mock_response_502.headers = {}
    mock_response_502.text = "bad gateway"
    mock_response_502.raise_for_status = Mock(
        side_effect=httpx.HTTPStatusError("502", request=Mock(), response=mock_response_502)
    )

    mock_post = AsyncMock(return_value=mock_response_502)
    monkeypatch.setattr(client._client, "post", mock_post)

    with pytest.raises(ModelRouterFailedError) as exc_info:
        await client.complete(prompt="test", evidence_refs=[], model=None)

    assert exc_info.value.status_code == 502
    assert mock_post.call_count == 4
