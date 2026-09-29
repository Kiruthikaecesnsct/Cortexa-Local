import asyncio

import httpx

from evidence.domain.errors.evidence_errors import CorpusLoadError
from evidence.domain.models.patent_corpus_record import PatentCorpusRecord
from evidence.infrastructure.config.settings import EvidenceSettings
from evidence.infrastructure.corpus.record_mapper import to_vector_item
from evidence.infrastructure.patent_apis.patent_adapter import build_retry


def _is_transient_status(status_code: int) -> bool:
    return status_code in {500, 502, 503, 504}


def _chunk(records: list[PatentCorpusRecord], size: int) -> list[list[PatentCorpusRecord]]:
    return [records[i : i + size] for i in range(0, len(records), size)]


class CorpusLoaderAdapter:
    def __init__(self, settings: EvidenceSettings, client: httpx.AsyncClient | None = None) -> None:
        self._settings = settings
        self._client = client or httpx.AsyncClient(
            timeout=httpx.Timeout(settings.corpus_load_timeout_seconds)
        )
        self._semaphore = asyncio.Semaphore(settings.corpus_max_concurrency)
        self._retry = build_retry(settings.corpus_load_max_retries)

    async def aclose(self) -> None:
        await self._client.aclose()

    async def embed_and_upsert(self, records: list[PatentCorpusRecord]) -> int:
        if not records:
            return 0
        batches = _chunk(records, self._settings.corpus_load_batch_size)
        total = 0
        for batch in batches:
            total += await self._load_batch(batch, total)
        return total

    async def _load_batch(self, batch: list[PatentCorpusRecord], loaded_so_far: int) -> int:
        try:
            decorated = self._retry(self._execute_batch)
            return await decorated(batch)
        except httpx.HTTPStatusError as exc:
            raise CorpusLoadError(
                f"Corpus load failed with status {exc.response.status_code}",
                status_code=exc.response.status_code,
                loaded_count=loaded_so_far,
            ) from exc
        except CorpusLoadError as exc:
            exc.loaded_count = loaded_so_far
            raise

    async def _execute_batch(self, batch: list[PatentCorpusRecord]) -> int:
        async with self._semaphore:
            vectors = await self._embed(batch)
            items = [to_vector_item(record, vector) for record, vector in zip(batch, vectors)]
            return await self._upsert(items)

    async def _embed(self, batch: list[PatentCorpusRecord]) -> list[list[float]]:
        url = f"{self._settings.vector_router_url}/embed"
        texts = [record.embedding_text() for record in batch]
        response = await self._client.post(url, json={"texts": texts})
        self._raise_for_status(response, "embed")
        data = response.json()
        embeddings: list[list[float]] = data.get("embeddings", [])
        if len(embeddings) != len(batch):
            raise CorpusLoadError("Vector router returned mismatched embedding count")
        return embeddings

    async def _upsert(self, items: list[dict]) -> int:
        url = f"{self._settings.vector_router_url}/upsert"
        response = await self._client.post(url, json={"items": items})
        self._raise_for_status(response, "upsert")
        return response.json().get("upserted", 0)

    def _raise_for_status(self, response: httpx.Response, operation: str) -> None:
        if response.is_success:
            return
        status = response.status_code
        if _is_transient_status(status):
            response.raise_for_status()
        raise CorpusLoadError(
            f"Corpus {operation} failed with status {status}",
            status_code=status,
        )
