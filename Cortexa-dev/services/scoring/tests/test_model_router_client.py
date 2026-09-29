import inspect
from unittest.mock import AsyncMock, MagicMock, patch

import httpx
import pytest

from scoring.domain.errors.scoring_errors import DualScoringFailedError, SingleScoringFailedError
from scoring.infrastructure.clients.dual_response import DualCompleteResponse, ModelResult
from scoring.infrastructure.clients.model_router_client import ModelRouterClient, RetryConfig
from scoring.infrastructure.clients.single_response import SingleCompleteResponse


def _make_http_client(json_body: dict, status_code: int = 200) -> MagicMock:
    response = MagicMock()
    response.is_success = 200 <= status_code < 300
    response.status_code = status_code
    response.json.return_value = json_body
    response.text = ""
    response.headers = {}
    response.request = MagicMock()
    http_client = MagicMock()
    http_client.post = AsyncMock(return_value=response)
    return http_client


def _make_client_with_no_retry() -> ModelRouterClient:
    http_client = MagicMock()
    return ModelRouterClient(
        client=http_client,
        retry_config=RetryConfig(
            max_retries=1,
            backoff_max_seconds=0.0,
            honor_retry_after=False,
            total_retry_budget_seconds=0.0,
        ),
    )


@pytest.mark.asyncio
async def test_complete_single_omits_model_key_when_not_provided():
    http_client = _make_http_client(
        {"provider": "azure-foundry", "model": "gpt-5.5", "content": "{}", "error": None}
    )
    client = _make_client_with_no_retry()
    client._client = http_client

    result = await client.complete_single("a prompt")

    _, kwargs = http_client.post.call_args
    assert "model" not in kwargs["json"]
    assert isinstance(result, SingleCompleteResponse)


@pytest.mark.asyncio
async def test_complete_single_includes_model_key_when_provided():
    http_client = _make_http_client(
        {"provider": "azure-foundry", "model": "gpt-5.4", "content": "{}", "error": None}
    )
    client = _make_client_with_no_retry()
    client._client = http_client

    await client.complete_single("a prompt", "gpt-5.4")

    _, kwargs = http_client.post.call_args
    assert kwargs["json"]["model"] == "gpt-5.4"


@pytest.mark.asyncio
async def test_complete_single_includes_model_key_when_empty_string():
    http_client = _make_http_client(
        {"provider": "azure-foundry", "model": "", "content": "{}", "error": None}
    )
    client = _make_client_with_no_retry()
    client._client = http_client

    await client.complete_single("a prompt", "")

    _, kwargs = http_client.post.call_args
    assert kwargs["json"]["model"] == ""


def test_complete_dual_signature_has_no_model_parameter():
    signature = inspect.signature(ModelRouterClient.complete_dual)

    assert "model" not in signature.parameters


@pytest.mark.asyncio
async def test_complete_single_raises_on_request_error():
    http_client = MagicMock()
    http_client.post = AsyncMock(side_effect=httpx.RequestError("boom"))
    client = _make_client_with_no_retry()
    client._client = http_client

    with pytest.raises(SingleScoringFailedError):
        await client.complete_single("a prompt")


@pytest.mark.asyncio
async def test_complete_dual_never_sends_model_key():
    http_client = _make_http_client(
        {
            "primary": {"provider": "azure", "model": "gpt-5.4", "content": "{}", "error": None},
            "secondary": {
                "provider": "anthropic",
                "model": "claude-3-5-sonnet",
                "content": "{}",
                "error": None,
            },
        }
    )
    client = _make_client_with_no_retry()
    client._client = http_client

    result = await client.complete_dual("a prompt")

    _, kwargs = http_client.post.call_args
    assert "model" not in kwargs["json"]
    assert isinstance(result, DualCompleteResponse)
    assert isinstance(result.primary, ModelResult)


@pytest.mark.asyncio
async def test_complete_dual_raises_dual_scoring_failed_on_502():
    http_client = _make_http_client({}, status_code=502)
    client = _make_client_with_no_retry()
    client._client = http_client

    with pytest.raises(DualScoringFailedError):
        await client.complete_dual("a prompt")


@pytest.mark.asyncio
async def test_complete_single_retries_429_with_retry_after_header():
    success_response = MagicMock()
    success_response.is_success = True
    success_response.status_code = 200
    success_response.json.return_value = {
        "provider": "azure-foundry",
        "model": "gpt-5.5",
        "content": "{}",
        "error": None,
    }
    success_response.headers = {}

    retry_response = MagicMock()
    retry_response.is_success = False
    retry_response.status_code = 429
    retry_response.text = "rate limit"
    retry_response.headers = {"Retry-After": "2.0"}
    retry_response.request = MagicMock()

    http_client = MagicMock()
    http_client.post = AsyncMock(side_effect=[retry_response, success_response])

    client = ModelRouterClient(
        client=http_client,
        retry_config=RetryConfig(
            max_retries=3,
            backoff_max_seconds=5.0,
            honor_retry_after=True,
            total_retry_budget_seconds=30.0,
        ),
    )

    with patch("asyncio.sleep") as mock_sleep:
        result = await client.complete_single("prompt")

    assert isinstance(result, SingleCompleteResponse)
    assert http_client.post.call_count == 2
    assert mock_sleep.await_count >= 1
    sleep_calls = [call[0][0] for call in mock_sleep.call_args_list]
    assert 2.0 in sleep_calls


@pytest.mark.asyncio
async def test_complete_single_retries_503_and_succeeds():
    success_response = MagicMock()
    success_response.is_success = True
    success_response.status_code = 200
    success_response.json.return_value = {
        "provider": "azure-foundry",
        "model": "gpt-5.5",
        "content": "{}",
        "error": None,
    }
    success_response.headers = {}

    retry_response = MagicMock()
    retry_response.is_success = False
    retry_response.status_code = 503
    retry_response.text = "service unavailable"
    retry_response.headers = {}
    retry_response.request = MagicMock()

    http_client = MagicMock()
    http_client.post = AsyncMock(side_effect=[retry_response, success_response])

    client = ModelRouterClient(
        client=http_client,
        retry_config=RetryConfig(
            max_retries=3,
            backoff_max_seconds=5.0,
            honor_retry_after=False,
            total_retry_budget_seconds=30.0,
        ),
    )

    result = await client.complete_single("prompt")

    assert isinstance(result, SingleCompleteResponse)
    assert http_client.post.call_count == 2


@pytest.mark.asyncio
async def test_complete_single_caps_retry_after_at_backoff_max():
    http_client = MagicMock()
    retry_response = MagicMock()
    retry_response.is_success = False
    retry_response.status_code = 429
    retry_response.text = "rate limit"
    retry_response.headers = {"Retry-After": "100.0"}
    retry_response.request = MagicMock()
    http_client.post = AsyncMock(return_value=retry_response)

    client = ModelRouterClient(
        client=http_client,
        retry_config=RetryConfig(
            max_retries=2,
            backoff_max_seconds=10.0,
            honor_retry_after=True,
            total_retry_budget_seconds=30.0,
        ),
    )

    with patch("asyncio.sleep") as mock_sleep:
        with pytest.raises(SingleScoringFailedError):
            await client.complete_single("prompt")

    assert mock_sleep.call_count > 0
    for call in mock_sleep.call_args_list:
        assert call[0][0] <= 10.0


@pytest.mark.asyncio
async def test_complete_single_exhausts_retry_budget():
    http_client = MagicMock()
    retry_response = MagicMock()
    retry_response.is_success = False
    retry_response.status_code = 503
    retry_response.text = "unavailable"
    retry_response.headers = {}
    retry_response.request = MagicMock()
    http_client.post = AsyncMock(return_value=retry_response)

    client = ModelRouterClient(
        client=http_client,
        retry_config=RetryConfig(
            max_retries=10,
            backoff_max_seconds=5.0,
            honor_retry_after=False,
            total_retry_budget_seconds=0.5,
        ),
    )

    with pytest.raises(SingleScoringFailedError):
        await client.complete_single("prompt")

    assert http_client.post.call_count < 10


@pytest.mark.asyncio
async def test_complete_dual_retries_429_and_succeeds():
    success_response = MagicMock()
    success_response.is_success = True
    success_response.status_code = 200
    success_response.json.return_value = {
        "primary": {"provider": "azure", "model": "gpt-5.4", "content": "{}", "error": None},
        "secondary": {
            "provider": "anthropic",
            "model": "claude-3-5-sonnet",
            "content": "{}",
            "error": None,
        },
    }
    success_response.headers = {}

    retry_response = MagicMock()
    retry_response.is_success = False
    retry_response.status_code = 429
    retry_response.text = "rate limit"
    retry_response.headers = {"Retry-After": "1.0"}
    retry_response.request = MagicMock()

    http_client = MagicMock()
    http_client.post = AsyncMock(side_effect=[retry_response, success_response])

    client = ModelRouterClient(
        client=http_client,
        retry_config=RetryConfig(
            max_retries=3,
            backoff_max_seconds=5.0,
            honor_retry_after=True,
            total_retry_budget_seconds=30.0,
        ),
    )

    result = await client.complete_dual("prompt")

    assert isinstance(result, DualCompleteResponse)
    assert http_client.post.call_count == 2


@pytest.mark.asyncio
async def test_complete_single_raises_with_400_status_code():
    http_client = MagicMock()
    response = MagicMock()
    response.is_success = False
    response.status_code = 400
    response.text = "content_filter"
    response.request = MagicMock()
    http_client.post = AsyncMock(return_value=response)
    client = _make_client_with_no_retry()
    client._client = http_client

    with pytest.raises(SingleScoringFailedError) as exc_info:
        await client.complete_single("a prompt")

    assert exc_info.value.status_code == 400


@pytest.mark.asyncio
async def test_complete_dual_raises_with_400_status_code():
    http_client = MagicMock()
    response = MagicMock()
    response.is_success = False
    response.status_code = 400
    response.text = "content_filter"
    response.request = MagicMock()
    http_client.post = AsyncMock(return_value=response)
    client = _make_client_with_no_retry()
    client._client = http_client

    with pytest.raises(DualScoringFailedError) as exc_info:
        await client.complete_dual("a prompt")

    assert exc_info.value.status_code == 400


@pytest.mark.asyncio
async def test_complete_single_raises_with_500_status_code():
    http_client = MagicMock()
    response = MagicMock()
    response.is_success = False
    response.status_code = 500
    response.text = "internal error"
    response.request = MagicMock()
    response.headers = {}
    http_client.post = AsyncMock(return_value=response)
    client = _make_client_with_no_retry()
    client._client = http_client

    with pytest.raises(SingleScoringFailedError) as exc_info:
        await client.complete_single("a prompt")

    assert exc_info.value.status_code == 500


@pytest.mark.asyncio
async def test_complete_single_does_not_retry_400():
    http_client = MagicMock()
    response = MagicMock()
    response.is_success = False
    response.status_code = 400
    response.text = "content_filter"
    response.request = MagicMock()
    http_client.post = AsyncMock(return_value=response)

    client = ModelRouterClient(
        client=http_client,
        retry_config=RetryConfig(
            max_retries=5,
            backoff_max_seconds=5.0,
            honor_retry_after=False,
            total_retry_budget_seconds=30.0,
        ),
    )

    with pytest.raises(SingleScoringFailedError):
        await client.complete_single("a prompt")

    assert http_client.post.call_count == 1
