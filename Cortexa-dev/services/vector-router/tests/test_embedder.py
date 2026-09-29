from unittest.mock import AsyncMock, MagicMock, patch

import httpx
import pytest

from vector_router.infrastructure.embedding.embedder import (
    _COGNITIVE_SERVICES_SCOPE,
    Embedder,
)

ENDPOINT = "https://test-oai.openai.azure.com"
DEPLOYMENT = "text-embedding-ada-002"
DIMENSIONS = 1536


@pytest.fixture
def mock_credential() -> MagicMock:
    return MagicMock()


@pytest.fixture
def mock_token_provider() -> AsyncMock:
    provider = AsyncMock()
    provider.return_value = "fake-token-12345"
    return provider


@pytest.fixture
def embedder(mock_credential, mock_token_provider) -> Embedder:
    with patch(
        "vector_router.infrastructure.embedding.embedder.get_bearer_token_provider",
        return_value=mock_token_provider,
    ) as mock_get:
        embedder_instance = Embedder(
            endpoint=ENDPOINT,
            credential=mock_credential,
            deployment=DEPLOYMENT,
            dimensions=DIMENSIONS,
        )
        mock_get.assert_called_once_with(mock_credential, _COGNITIVE_SERVICES_SCOPE)
        return embedder_instance


async def test_embed_uses_bearer_token_and_correct_url(embedder, mock_token_provider):
    texts = ["hello world", "test input"]
    mock_response = MagicMock()
    mock_response.status_code = 200
    mock_response.json.return_value = {
        "data": [
            {"embedding": [0.1, 0.2, 0.3]},
            {"embedding": [0.4, 0.5, 0.6]},
        ]
    }

    with patch("httpx.AsyncClient.post", new_callable=AsyncMock) as mock_post:
        mock_post.return_value = mock_response

        await embedder.embed(texts)

        mock_token_provider.assert_called_once()

        expected_url = (
            f"{ENDPOINT}/openai/deployments/{DEPLOYMENT}/embeddings?api-version=2024-10-21"
        )
        expected_headers = {
            "Authorization": "Bearer fake-token-12345",
            "Content-Type": "application/json",
        }
        expected_payload = {"input": texts, "dimensions": DIMENSIONS}

        mock_post.assert_called_once()
        call_args = mock_post.call_args

        assert call_args.args[0] == expected_url
        assert call_args.kwargs["json"] == expected_payload
        assert call_args.kwargs["headers"] == expected_headers

        timeout = embedder._timeout
        assert isinstance(timeout, httpx.Timeout)
        assert timeout.connect == 10.0
        assert timeout.read == 60.0
        assert timeout.write == 10.0
        assert timeout.pool == 10.0

        assert "api-key" not in call_args.kwargs.get("headers", {})


async def test_embed_returns_list_of_embeddings(embedder, mock_token_provider):
    texts = ["first", "second"]
    expected_embeddings = [[0.1, 0.2], [0.3, 0.4]]
    mock_response = MagicMock()
    mock_response.status_code = 200
    mock_response.json.return_value = {
        "data": [
            {"embedding": expected_embeddings[0]},
            {"embedding": expected_embeddings[1]},
        ]
    }

    with patch("httpx.AsyncClient.post", new_callable=AsyncMock) as mock_post:
        mock_post.return_value = mock_response

        result = await embedder.embed(texts)

        assert result == expected_embeddings


async def test_embed_token_provider_reused_across_calls(mock_credential):
    with patch(
        "vector_router.infrastructure.embedding.embedder.get_bearer_token_provider"
    ) as mock_get:
        mock_provider = AsyncMock(return_value="token-xyz")
        mock_get.return_value = mock_provider

        embedder = Embedder(
            endpoint=ENDPOINT,
            credential=mock_credential,
            deployment=DEPLOYMENT,
            dimensions=DIMENSIONS,
        )

        mock_get.assert_called_once_with(mock_credential, _COGNITIVE_SERVICES_SCOPE)

        mock_response = MagicMock()
        mock_response.status_code = 200
        mock_response.json.return_value = {"data": [{"embedding": [0.1]}]}

        with patch("httpx.AsyncClient.post", new_callable=AsyncMock) as mock_post:
            mock_post.return_value = mock_response

            await embedder.embed(["first"])
            await embedder.embed(["second"])

            mock_get.assert_called_once()
            assert mock_provider.call_count == 2


async def test_embed_http_error_propagates(embedder, mock_token_provider):
    texts = ["test"]
    mock_response = MagicMock()
    mock_response.status_code = 401
    mock_response.raise_for_status.side_effect = httpx.HTTPStatusError(
        "Unauthorized", request=MagicMock(), response=mock_response
    )

    with patch("httpx.AsyncClient.post", new_callable=AsyncMock) as mock_post:
        mock_post.return_value = mock_response

        with pytest.raises(httpx.HTTPStatusError):
            await embedder.embed(texts)


def _response(status_code: int, *, headers=None, embeddings=None) -> MagicMock:
    resp = MagicMock()
    resp.status_code = status_code
    resp.headers = headers or {}
    resp.json.return_value = {"data": [{"embedding": e} for e in (embeddings or [])]}
    return resp


async def test_embed_retries_on_429_then_succeeds(embedder, mock_token_provider):
    throttled = _response(429, headers={"Retry-After": "2"})
    ok = _response(200, embeddings=[[0.1, 0.2]])

    with (
        patch("httpx.AsyncClient.post", new_callable=AsyncMock) as mock_post,
        patch(
            "vector_router.infrastructure.embedding.embedder.asyncio.sleep",
            new_callable=AsyncMock,
        ) as mock_sleep,
    ):
        mock_post.side_effect = [throttled, ok]

        result = await embedder.embed(["hi"])

        assert result == [[0.1, 0.2]]
        assert mock_post.call_count == 2
        # Honours the Retry-After header value.
        mock_sleep.assert_awaited_once_with(2.0)


async def test_embed_gives_up_after_max_retries(mock_credential):
    with patch(
        "vector_router.infrastructure.embedding.embedder.get_bearer_token_provider",
        return_value=AsyncMock(return_value="tok"),
    ):
        embedder = Embedder(
            endpoint=ENDPOINT,
            credential=mock_credential,
            deployment=DEPLOYMENT,
            dimensions=DIMENSIONS,
            max_retries=2,
        )

    throttled = _response(429)
    throttled.raise_for_status.side_effect = httpx.HTTPStatusError(
        "Too Many Requests", request=MagicMock(), response=throttled
    )

    with (
        patch("httpx.AsyncClient.post", new_callable=AsyncMock) as mock_post,
        patch(
            "vector_router.infrastructure.embedding.embedder.asyncio.sleep",
            new_callable=AsyncMock,
        ),
    ):
        mock_post.return_value = throttled

        with pytest.raises(httpx.HTTPStatusError):
            await embedder.embed(["hi"])

        # Initial attempt + 2 retries = 3 posts before giving up.
        assert mock_post.call_count == 3


async def test_embed_retries_on_read_timeout_then_succeeds(embedder, mock_token_provider):
    ok = _response(200, embeddings=[[0.7, 0.8]])

    with (
        patch("httpx.AsyncClient.post", new_callable=AsyncMock) as mock_post,
        patch(
            "vector_router.infrastructure.embedding.embedder.asyncio.sleep",
            new_callable=AsyncMock,
        ) as mock_sleep,
        patch(
            "vector_router.infrastructure.embedding.embedder.random.uniform",
            return_value=0.5,
        ),
    ):
        mock_post.side_effect = [
            httpx.ReadTimeout("ReadTimeout"),
            httpx.ReadTimeout("ReadTimeout"),
            ok,
        ]

        result = await embedder.embed(["test"])

        assert result == [[0.7, 0.8]]
        assert mock_post.call_count == 3
        assert mock_sleep.call_count == 2
        mock_sleep.assert_any_await(0.5)


async def test_embed_reraises_read_timeout_after_max_retries(mock_credential):
    with patch(
        "vector_router.infrastructure.embedding.embedder.get_bearer_token_provider",
        return_value=AsyncMock(return_value="tok"),
    ):
        embedder = Embedder(
            endpoint=ENDPOINT,
            credential=mock_credential,
            deployment=DEPLOYMENT,
            dimensions=DIMENSIONS,
            max_retries=2,
        )

    with (
        patch("httpx.AsyncClient.post", new_callable=AsyncMock) as mock_post,
        patch(
            "vector_router.infrastructure.embedding.embedder.asyncio.sleep",
            new_callable=AsyncMock,
        ),
    ):
        mock_post.side_effect = httpx.ReadTimeout("Persistent timeout")

        with pytest.raises(httpx.ReadTimeout):
            await embedder.embed(["test"])

        assert mock_post.call_count == 3


async def test_embed_retries_on_connect_error_then_succeeds(embedder, mock_token_provider):
    ok = _response(200, embeddings=[[0.3, 0.4]])

    with (
        patch("httpx.AsyncClient.post", new_callable=AsyncMock) as mock_post,
        patch(
            "vector_router.infrastructure.embedding.embedder.asyncio.sleep",
            new_callable=AsyncMock,
        ) as mock_sleep,
        patch(
            "vector_router.infrastructure.embedding.embedder.random.uniform",
            return_value=1.2,
        ),
    ):
        mock_post.side_effect = [httpx.ConnectError("Network unreachable"), ok]

        result = await embedder.embed(["retry"])

        assert result == [[0.3, 0.4]]
        assert mock_post.call_count == 2
        mock_sleep.assert_awaited_once_with(1.2)


async def test_embed_does_not_retry_non_transient_http_error(embedder, mock_token_provider):
    bad_request = _response(400)
    bad_request.raise_for_status.side_effect = httpx.HTTPStatusError(
        "Bad Request", request=MagicMock(), response=bad_request
    )

    with (
        patch("httpx.AsyncClient.post", new_callable=AsyncMock) as mock_post,
        patch(
            "vector_router.infrastructure.embedding.embedder.asyncio.sleep",
            new_callable=AsyncMock,
        ) as mock_sleep,
    ):
        mock_post.return_value = bad_request

        with pytest.raises(httpx.HTTPStatusError):
            await embedder.embed(["bad"])

        assert mock_post.call_count == 1
        mock_sleep.assert_not_awaited()


async def test_embed_exception_retry_full_jitter(mock_credential):
    const_jitter = 0.7

    with patch(
        "vector_router.infrastructure.embedding.embedder.get_bearer_token_provider",
        return_value=AsyncMock(return_value="tok"),
    ):
        embedder = Embedder(
            endpoint=ENDPOINT,
            credential=mock_credential,
            deployment=DEPLOYMENT,
            dimensions=DIMENSIONS,
            max_retries=3,
            backoff_base_seconds=2.0,
            backoff_max_seconds=16.0,
        )

    ok = _response(200, embeddings=[[0.1]])

    with (
        patch("httpx.AsyncClient.post", new_callable=AsyncMock) as mock_post,
        patch(
            "vector_router.infrastructure.embedding.embedder.asyncio.sleep",
            new_callable=AsyncMock,
        ) as mock_sleep,
        patch(
            "vector_router.infrastructure.embedding.embedder.random.uniform",
            return_value=const_jitter,
        ) as mock_uniform,
    ):
        mock_post.side_effect = [
            httpx.ReadTimeout("timeout"),
            httpx.ReadTimeout("timeout"),
            ok,
        ]

        result = await embedder.embed(["jitter"])

        assert result == [[0.1]]
        assert mock_post.call_count == 3

        assert mock_uniform.call_count == 2
        mock_uniform.assert_any_call(0, 2.0)
        mock_uniform.assert_any_call(0, 4.0)

        assert mock_sleep.call_count == 2
        for call in mock_sleep.await_args_list:
            assert call[0][0] == const_jitter
