import json

import httpx
import pytest
import respx

from evidence.domain.errors.evidence_errors import CorpusLoadError
from evidence.domain.models.patent_corpus_record import PatentCorpusRecord
from evidence.infrastructure.config.settings import EvidenceSettings
from evidence.infrastructure.corpus.corpus_loader_adapter import CorpusLoaderAdapter

VECTOR_ROUTER_URL = "http://test-vector-router"


def _make_records(count: int) -> list[PatentCorpusRecord]:
    return [
        PatentCorpusRecord(
            reference=f"US{1000 + i}",
            title=f"Patent {i}",
            abstract="An abstract.",
            applicant="Acme Corp",
            date="2024-01-01",
            url=f"https://patents.example.com/US{1000 + i}",
            metadata={"jurisdiction": "US"},
        )
        for i in range(count)
    ]


async def test_embed_and_upsert_calls_embed_then_upsert_in_order(
    corpus_load_settings, mock_vector_router_load
):
    records = _make_records(2)
    async with httpx.AsyncClient() as client:
        adapter = CorpusLoaderAdapter(corpus_load_settings, client)
        total = await adapter.embed_and_upsert(records)

    assert total == 2
    embed_call = mock_vector_router_load.calls[0]
    upsert_call = mock_vector_router_load.calls[1]
    assert embed_call.request.url.path == "/embed"
    assert upsert_call.request.url.path == "/upsert"


async def test_embed_and_upsert_batches_records(corpus_load_settings, mock_vector_router_load):
    records = _make_records(5)
    async with httpx.AsyncClient() as client:
        adapter = CorpusLoaderAdapter(corpus_load_settings, client)
        total = await adapter.embed_and_upsert(records)

    assert total == 5
    embed_calls = [c for c in mock_vector_router_load.calls if c.request.url.path == "/embed"]
    assert len(embed_calls) == 3


async def test_empty_records_returns_zero_without_calls(
    corpus_load_settings, mock_vector_router_load
):
    async with httpx.AsyncClient() as client:
        adapter = CorpusLoaderAdapter(corpus_load_settings, client)
        total = await adapter.embed_and_upsert([])

    assert total == 0
    assert mock_vector_router_load.calls.call_count == 0


async def test_upsert_500_retries_then_raises_corpus_load_error(
    corpus_load_settings, mock_vector_router_load_500
):
    records = _make_records(1)
    async with httpx.AsyncClient() as client:
        adapter = CorpusLoaderAdapter(corpus_load_settings, client)
        with pytest.raises(CorpusLoadError):
            await adapter.embed_and_upsert(records)

    upsert_calls = [c for c in mock_vector_router_load_500.calls if c.request.url.path == "/upsert"]
    assert len(upsert_calls) >= 2


async def test_failure_on_later_batch_preserves_earlier_loaded_count(
    corpus_load_settings, mock_vector_router_load
):
    records = _make_records(3)
    call_count = 0
    upsert_route = mock_vector_router_load.routes[1]

    def _flaky_upsert(request: httpx.Request) -> httpx.Response:
        nonlocal call_count
        call_count += 1
        if call_count == 2:
            return httpx.Response(400, json={"detail": "rejected"})
        items = json.loads(request.content)["items"]
        return httpx.Response(200, json={"upserted": len(items)})

    upsert_route.side_effect = _flaky_upsert

    async with httpx.AsyncClient() as client:
        adapter = CorpusLoaderAdapter(corpus_load_settings, client)
        with pytest.raises(CorpusLoadError) as exc_info:
            await adapter.embed_and_upsert(records)

    assert exc_info.value.loaded_count == 2


async def test_idempotent_load_uses_reference_as_vector_id(
    corpus_load_settings, mock_vector_router_load
):
    records = _make_records(1)
    async with httpx.AsyncClient() as client:
        adapter = CorpusLoaderAdapter(corpus_load_settings, client)
        await adapter.embed_and_upsert(records)

    upsert_call = next(c for c in mock_vector_router_load.calls if c.request.url.path == "/upsert")
    body = json.loads(upsert_call.request.content)
    assert body["items"][0]["id"] == "US1000"


async def test_embed_and_upsert_url_built_from_vector_router_url_field():
    custom_base = "http://custom-vector-router"
    settings = EvidenceSettings(
        vector_router_url=custom_base,
        model_router_url="http://test-model-router",
        corpus_load_batch_size=2,
    )
    records = _make_records(1)

    with respx.mock(base_url=custom_base, assert_all_called=False) as mock:
        embed_route = mock.post("/embed").mock(
            return_value=httpx.Response(200, json={"embeddings": [[0.1, 0.2, 0.3]]})
        )
        upsert_route = mock.post("/upsert").mock(
            return_value=httpx.Response(200, json={"upserted": 1})
        )

        async with httpx.AsyncClient() as client:
            adapter = CorpusLoaderAdapter(settings, client)
            total = await adapter.embed_and_upsert(records)

    assert total == 1
    assert embed_route.called
    assert upsert_route.called
