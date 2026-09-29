import json

import httpx
import pytest

from seeding.domain.errors.seeding_errors import (
    EvidenceServicePermanentError,
    EvidenceServiceTransientError,
)
from seeding.infrastructure.clients.evidence_client import EvidenceClient

_BASE_URL = "http://evidence.internal.test"


def _client_with_handler(handler) -> EvidenceClient:
    client = EvidenceClient(base_url=_BASE_URL, timeout=5.0)
    client._client = httpx.AsyncClient(base_url=_BASE_URL, transport=httpx.MockTransport(handler))
    return client


async def test_search_parses_matches_and_sources():
    captured = {}

    def _handler(request: httpx.Request) -> httpx.Response:
        captured["body"] = json.loads(request.content)
        return httpx.Response(
            200,
            json={
                "matches": [
                    {
                        "reference": "US1",
                        "title": "T",
                        "applicant": "Acme",
                        "date": "2020",
                        "url": "http://x",
                        "relevance_score": 0.9,
                        "source": "USPTO",
                    }
                ],
                "sources": [
                    {"source": "USPTO", "outcome": "ok", "hit_count": 1, "error_detail": None}
                ],
            },
        )

    client = _client_with_handler(_handler)

    result = await client.search_patents("query", 25)

    assert captured["body"] == {"query": "query", "limit": 25}
    assert result.matches[0].reference == "US1"
    assert result.matches[0].relevance_score == 0.9
    assert result.sources[0].outcome == "ok"
    await client.aclose()


async def test_search_drops_match_without_reference_and_tolerates_extra_fields():
    def _handler(request: httpx.Request) -> httpx.Response:
        return httpx.Response(
            200,
            json={
                "matches": [
                    {"title": "no ref"},
                    {"reference": "US2", "unexpected": "ignored"},
                ],
                "sources": [{"source": "Lens", "status": "api_error", "error": "boom"}],
            },
        )

    client = _client_with_handler(_handler)

    result = await client.search_patents("q", 10)

    assert len(result.matches) == 1
    assert result.matches[0].reference == "US2"
    assert result.sources[0].outcome == "api_error"
    assert result.sources[0].error_detail == "boom"
    await client.aclose()


async def test_server_error_maps_to_transient():
    def _handler(request: httpx.Request) -> httpx.Response:
        return httpx.Response(503, text="unavailable")

    client = _client_with_handler(_handler)

    with pytest.raises(EvidenceServiceTransientError) as exc_info:
        await client.search_patents("q", 10)

    assert exc_info.value.status_code == 503
    await client.aclose()


async def test_rate_limit_maps_to_transient():
    def _handler(request: httpx.Request) -> httpx.Response:
        return httpx.Response(429, text="slow down")

    client = _client_with_handler(_handler)

    with pytest.raises(EvidenceServiceTransientError):
        await client.search_patents("q", 10)
    await client.aclose()


async def test_client_error_maps_to_permanent():
    def _handler(request: httpx.Request) -> httpx.Response:
        return httpx.Response(400, text="bad request")

    client = _client_with_handler(_handler)

    with pytest.raises(EvidenceServicePermanentError) as exc_info:
        await client.search_patents("q", 10)

    assert exc_info.value.status_code == 400
    await client.aclose()


async def test_timeout_maps_to_transient():
    def _handler(request: httpx.Request) -> httpx.Response:
        raise httpx.ReadTimeout("timed out", request=request)

    client = _client_with_handler(_handler)

    with pytest.raises(EvidenceServiceTransientError):
        await client.search_patents("q", 10)
    await client.aclose()
