import asyncio

import httpx

from evidence.domain.enums.evidence_source import EvidenceSource
from evidence.domain.errors.evidence_errors import CorpusSearchError
from evidence.domain.models.patent_match import PatentMatch
from evidence.domain.services.patent_url import canonical_google_patents_url
from evidence.infrastructure.config.settings import EvidenceSettings
from evidence.infrastructure.patent_apis.patent_adapter import build_retry


def _clamp_score(value: float) -> float:
    return max(0.0, min(1.0, value))


def _is_transient_status(status_code: int) -> bool:
    return status_code in {500, 502, 503, 504}


class CorpusAdapter:
    def __init__(self, settings: EvidenceSettings, client: httpx.AsyncClient | None = None) -> None:
        self._settings = settings
        self._client = client or httpx.AsyncClient(
            timeout=httpx.Timeout(settings.corpus_search_timeout_seconds)
        )
        self._semaphore = asyncio.Semaphore(settings.corpus_max_concurrency)
        self._retry = build_retry(settings.corpus_max_retries)

    async def search(self, query: str, limit: int) -> list[PatentMatch]:
        try:
            decorated = self._retry(self._execute)
            return await decorated(query, limit)
        except httpx.HTTPStatusError as exc:
            raise CorpusSearchError(
                f"Corpus search failed with status {exc.response.status_code}",
                status_code=exc.response.status_code,
            ) from exc

    async def _execute(self, query: str, limit: int) -> list[PatentMatch]:
        async with self._semaphore:
            embedding = await self._embed(query)
            hits = await self._vector_search(embedding, limit)
        return [self._map_hit(hit) for hit in hits]

    async def _embed(self, query: str) -> list[float]:
        url = f"{self._settings.vector_router_url}/embed"
        response = await self._client.post(url, json={"texts": [query]})
        self._raise_for_status(response, "embed")
        data = response.json()
        embeddings: list[list[float]] = data.get("embeddings", [])
        if not embeddings:
            raise CorpusSearchError("Vector router returned no embeddings")
        return embeddings[0]

    async def _vector_search(self, embedding: list[float], limit: int) -> list[dict]:
        url = f"{self._settings.vector_router_url}/search"
        response = await self._client.post(url, json={"embedding": embedding, "top_k": limit})
        self._raise_for_status(response, "search")
        return response.json().get("hits", [])

    def _raise_for_status(self, response: httpx.Response, operation: str) -> None:
        if response.is_success:
            return
        status = response.status_code
        if _is_transient_status(status):
            response.raise_for_status()
        raise CorpusSearchError(
            f"Corpus {operation} failed with status {status}",
            status_code=status,
        )

    def _map_hit(self, hit: dict) -> PatentMatch:
        reference = hit.get("id")
        if not reference:
            raise CorpusSearchError("Vector router hit missing required field 'id'")
        score = hit.get("score")
        if score is None:
            raise CorpusSearchError("Vector router hit missing required field 'score'")
        payload: dict = hit.get("payload", {})
        metadata: dict = payload.get("metadata", {})
        return PatentMatch(
            reference=reference,
            title=payload.get("title", ""),
            applicant=payload.get("applicant", ""),
            date=payload.get("date", ""),
            url=canonical_google_patents_url(reference),
            relevance_score=_clamp_score(score),
            source=EvidenceSource.SeedCorpus,
            jurisdiction=metadata.get("jurisdiction", ""),
            abstract=payload.get("abstract", ""),
            claims=payload.get("claims", []),
        )
