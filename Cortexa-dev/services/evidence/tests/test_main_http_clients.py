import httpx
import pytest

from evidence.infrastructure.config.settings import EvidenceSettings
from evidence.main import HttpClients, _build_http_clients


@pytest.mark.asyncio
async def test_build_http_clients_timeouts():
    settings = EvidenceSettings(
        model_router_url="http://model-router.internal",
        vector_router_url="http://vector-router.internal",
        patent_api_timeout_seconds=15.0,
        corpus_search_timeout_seconds=20.0,
        llm_research_timeout_seconds=170.0,
    )

    clients = _build_http_clients(settings)

    assert isinstance(clients, HttpClients)
    assert clients.uspto.timeout == httpx.Timeout(15.0)
    assert clients.epo.timeout == httpx.Timeout(15.0)
    assert clients.lens.timeout == httpx.Timeout(15.0)
    assert clients.corpus.timeout == httpx.Timeout(20.0)
    assert clients.llm.timeout == httpx.Timeout(170.0)

    await clients.uspto.aclose()
    await clients.epo.aclose()
    await clients.lens.aclose()
    await clients.corpus.aclose()
    await clients.llm.aclose()
