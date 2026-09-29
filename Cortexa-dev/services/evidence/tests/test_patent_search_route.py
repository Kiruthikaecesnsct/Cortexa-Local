import httpx
from fastapi import FastAPI

from evidence.api.patent_search_routes import router
from evidence.domain.enums.evidence_source import EvidenceSource
from evidence.domain.enums.patent_source_name import PatentSourceName
from evidence.domain.enums.patent_source_outcome import PatentSourceOutcome
from evidence.domain.models.patent_match import PatentMatch
from evidence.domain.models.patent_source_result import PatentSourceResult

SEARCH_URL = "/evidence/patents/search"
OK_HIT_COUNT = 2
FAILED_HIT_COUNT = 0
CAP_LIMIT = 50


class FakeAdapter:
    def __init__(
        self,
        matches: list[PatentMatch],
        sources: list[PatentSourceResult],
    ) -> None:
        self._matches = matches
        self._sources = sources
        self.calls: list[tuple[str, int]] = []

    async def search(
        self, query: str, limit: int
    ) -> tuple[list[PatentMatch], list[PatentSourceResult]]:
        self.calls.append((query, limit))
        return self._matches, self._sources


def _match(reference: str) -> PatentMatch:
    return PatentMatch(
        reference=reference,
        title="Sparse tensor quantization",
        applicant="Acme Corp",
        date="2024-03-15",
        url=f"https://patents.example/{reference}",
        relevance_score=0.75,
        source=EvidenceSource.PatentApi,
        jurisdiction="US",
        abstract="An abstract.",
        claims=["A method."],
    )


def _ok_source(source: PatentSourceName) -> PatentSourceResult:
    return PatentSourceResult(
        source=source,
        outcome=PatentSourceOutcome.ok,
        hit_count=OK_HIT_COUNT,
        latency_ms=42.0,
    )


def _failed_source(source: PatentSourceName) -> PatentSourceResult:
    return PatentSourceResult(
        source=source,
        outcome=PatentSourceOutcome.timeout,
        hit_count=FAILED_HIT_COUNT,
        latency_ms=60000.0,
        error_detail="request timed out",
    )


def _client(adapter: FakeAdapter) -> httpx.AsyncClient:
    app = FastAPI()
    app.include_router(router)
    app.state.patent_adapter = adapter
    transport = httpx.ASGITransport(app=app)
    return httpx.AsyncClient(transport=transport, base_url="http://test")


async def test_search_happy_path_returns_matches_and_sources() -> None:
    adapter = FakeAdapter(
        matches=[_match("US11234567"), _match("US11234568")],
        sources=[_ok_source(PatentSourceName.USPTO), _ok_source(PatentSourceName.EPO)],
    )

    async with _client(adapter) as client:
        response = await client.post(SEARCH_URL, json={"query": "sparse tensor", "limit": 10})

    assert response.status_code == 200
    body = response.json()
    assert len(body["matches"]) == OK_HIT_COUNT
    assert body["matches"][0]["reference"] == "US11234567"
    assert {s["source"] for s in body["sources"]} == {"USPTO", "EPO"}
    assert adapter.calls == [("sparse tensor", 10)]


async def test_search_per_source_degrade_is_recorded_and_others_returned() -> None:
    adapter = FakeAdapter(
        matches=[_match("US11234567")],
        sources=[
            _ok_source(PatentSourceName.USPTO),
            _failed_source(PatentSourceName.EPO),
        ],
    )

    async with _client(adapter) as client:
        response = await client.post(SEARCH_URL, json={"query": "quantization", "limit": 5})

    assert response.status_code == 200
    body = response.json()
    assert len(body["matches"]) == 1
    sources = {s["source"]: s for s in body["sources"]}
    assert sources["USPTO"]["outcome"] == "ok"
    assert sources["EPO"]["outcome"] == "timeout"
    assert sources["EPO"]["error_detail"] == "request timed out"
    assert sources["EPO"]["hit_count"] == FAILED_HIT_COUNT


async def test_search_limit_is_capped() -> None:
    adapter = FakeAdapter(matches=[], sources=[])

    async with _client(adapter) as client:
        response = await client.post(SEARCH_URL, json={"query": "anything", "limit": 500})

    assert response.status_code == 200
    assert adapter.calls == [("anything", CAP_LIMIT)]


async def test_search_empty_query_returns_422() -> None:
    adapter = FakeAdapter(matches=[], sources=[])

    async with _client(adapter) as client:
        response = await client.post(SEARCH_URL, json={"query": "   ", "limit": 10})

    assert response.status_code == 422
    assert adapter.calls == []
