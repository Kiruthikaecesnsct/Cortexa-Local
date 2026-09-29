import httpx
import pytest

from seeding.domain.errors.seeding_errors import (
    VectorRouterPermanentError,
    VectorRouterTransientError,
)
from seeding.infrastructure.clients.vector_router_client import VectorRouterClient

_BASE_URL = "http://vector-router.internal.test"


def _client_with_handler(handler) -> VectorRouterClient:
    client = VectorRouterClient(base_url=_BASE_URL, timeout=5.0)
    client._client = httpx.AsyncClient(base_url=_BASE_URL, transport=httpx.MockTransport(handler))
    return client


async def test_embed_returns_embeddings():
    def _handler(request: httpx.Request) -> httpx.Response:
        return httpx.Response(200, json={"embeddings": [[0.1, 0.2], [0.3, 0.4]]})

    client = _client_with_handler(_handler)

    result = await client.embed(["a", "b"])

    assert result == [[0.1, 0.2], [0.3, 0.4]]
    await client.aclose()


async def test_upsert_posts_target_and_returns_count():
    captured = {}

    def _handler(request: httpx.Request) -> httpx.Response:
        import json

        captured["body"] = json.loads(request.content)
        return httpx.Response(200, json={"upserted": 3})

    client = _client_with_handler(_handler)

    count = await client.upsert([{"id": "x"}], target="asset")

    assert count == 3
    assert captured["body"]["target"] == "asset"
    await client.aclose()


async def test_server_error_maps_to_transient():
    def _handler(request: httpx.Request) -> httpx.Response:
        return httpx.Response(503, text="unavailable")

    client = _client_with_handler(_handler)

    with pytest.raises(VectorRouterTransientError) as exc_info:
        await client.embed(["a"])

    assert exc_info.value.status_code == 503
    await client.aclose()


async def test_rate_limit_maps_to_transient():
    def _handler(request: httpx.Request) -> httpx.Response:
        return httpx.Response(429, text="slow down")

    client = _client_with_handler(_handler)

    with pytest.raises(VectorRouterTransientError):
        await client.embed(["a"])
    await client.aclose()


async def test_client_error_maps_to_permanent():
    def _handler(request: httpx.Request) -> httpx.Response:
        return httpx.Response(400, text="bad request")

    client = _client_with_handler(_handler)

    with pytest.raises(VectorRouterPermanentError) as exc_info:
        await client.embed(["a"])

    assert exc_info.value.status_code == 400
    await client.aclose()


async def test_timeout_maps_to_transient():
    def _handler(request: httpx.Request) -> httpx.Response:
        raise httpx.ReadTimeout("timed out", request=request)

    client = _client_with_handler(_handler)

    with pytest.raises(VectorRouterTransientError):
        await client.embed(["a"])
    await client.aclose()
