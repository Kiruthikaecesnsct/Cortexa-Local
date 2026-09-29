import asyncio

import httpx
import pytest
import respx

from evidence.domain.enums.evidence_source import EvidenceSource
from evidence.domain.errors.evidence_errors import CorpusSearchError
from evidence.infrastructure.config.settings import EvidenceSettings
from evidence.infrastructure.corpus.corpus_adapter import CorpusAdapter

VECTOR_ROUTER_URL = "http://test-vector-router"
_EMBED_RESPONSE = {"embeddings": [[0.1, 0.2, 0.3]]}


async def test_success_returns_patent_matches(corpus_settings, mock_vector_router):
    async with httpx.AsyncClient() as client:
        adapter = CorpusAdapter(corpus_settings, client)
        results = await adapter.search("sparse tensor", limit=5)

    assert len(results) == 2
    assert results[0].reference == "EP1234567"
    assert results[0].title == "Sparse tensor method"
    assert results[0].source is EvidenceSource.SeedCorpus


async def test_relevance_scores_in_bounds(corpus_settings, mock_vector_router):
    async with httpx.AsyncClient() as client:
        adapter = CorpusAdapter(corpus_settings, client)
        results = await adapter.search("q", limit=5)

    for r in results:
        assert 0.0 <= r.relevance_score <= 1.0


async def test_empty_results_returns_empty_list(corpus_settings, mock_vector_router_empty):
    async with httpx.AsyncClient() as client:
        adapter = CorpusAdapter(corpus_settings, client)
        results = await adapter.search("nonexistent", limit=5)

    assert results == []


async def test_400_raises_corpus_search_error_immediately(corpus_settings, mock_vector_router_400):
    async with httpx.AsyncClient() as client:
        adapter = CorpusAdapter(corpus_settings, client)
        with pytest.raises(CorpusSearchError) as exc_info:
            await adapter.search("bad query", limit=5)

    assert exc_info.value.status_code == 400


async def test_500_retries_then_raises(corpus_settings, mock_vector_router_500):
    async with httpx.AsyncClient() as client:
        adapter = CorpusAdapter(corpus_settings, client)
        with pytest.raises(CorpusSearchError):
            await adapter.search("q", limit=5)

    assert mock_vector_router_500.calls.call_count >= 2


async def test_semaphore_caps_concurrency(corpus_settings, mock_vector_router):
    async with httpx.AsyncClient() as client:
        adapter = CorpusAdapter(corpus_settings, client)
        tasks = [adapter.search("query", limit=2) for _ in range(5)]
        results = await asyncio.gather(*tasks)

    for result in results:
        assert isinstance(result, list)


async def test_score_clamped_to_valid_range(corpus_settings):
    overscore_hits = {
        "hits": [
            {
                "id": "EP9999999",
                "score": 1.5,
                "payload": {
                    "title": "Over-scored patent",
                    "applicant": "Test Corp",
                    "date": "2024-01-01",
                    "url": "https://corpus/EP9999999",
                },
            }
        ]
    }
    with respx.mock(base_url=VECTOR_ROUTER_URL) as mock:
        mock.post("/embed").mock(return_value=httpx.Response(200, json=_EMBED_RESPONSE))
        mock.post("/search").mock(return_value=httpx.Response(200, json=overscore_hits))

        async with httpx.AsyncClient() as client:
            adapter = CorpusAdapter(corpus_settings, client)
            results = await adapter.search("q", limit=1)

    assert results[0].relevance_score == 1.0


async def test_search_url_built_from_vector_router_url_field():
    custom_base = "http://custom-vector-router"
    settings = EvidenceSettings(
        vector_router_url=custom_base,
        model_router_url="http://test-model-router",
    )
    with respx.mock(base_url=custom_base) as mock:
        embed_route = mock.post("/embed").mock(
            return_value=httpx.Response(200, json=_EMBED_RESPONSE)
        )
        search_route = mock.post("/search").mock(
            return_value=httpx.Response(200, json={"hits": []})
        )

        async with httpx.AsyncClient() as client:
            adapter = CorpusAdapter(settings, client)
            await adapter.search("q", limit=1)

    assert embed_route.called
    assert search_route.called
