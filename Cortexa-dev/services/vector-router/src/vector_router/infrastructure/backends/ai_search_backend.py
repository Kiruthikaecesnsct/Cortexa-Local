import json
from dataclasses import dataclass

from azure.core.credentials_async import AsyncTokenCredential
from azure.core.exceptions import ResourceNotFoundError
from azure.search.documents.aio import SearchClient
from azure.search.documents.indexes.aio import SearchIndexClient
from azure.search.documents.indexes.models import (
    HnswAlgorithmConfiguration,
    SearchField,
    SearchFieldDataType,
    SearchIndex,
    SimpleField,
    VectorSearch,
    VectorSearchProfile,
)

from ...domain.models import SearchFilter, VectorHit, VectorItem, VectorTarget
from ..embedding.embedder import Embedder

_DELETE_PAGE_SIZE = 1000


@dataclass(frozen=True)
class AiSearchBackendConfig:
    endpoint: str
    index_name: str
    asset_index_name: str
    dimensions: int


class AiSearchBackend:
    def __init__(
        self,
        config: AiSearchBackendConfig,
        credential: AsyncTokenCredential,
        embedder: Embedder,
    ) -> None:
        self._config = config
        self._credential = credential
        self._embedder = embedder

    async def _client(self) -> SearchClient:
        return SearchClient(
            endpoint=self._config.endpoint,
            index_name=self._config.index_name,
            credential=self._credential,
        )

    async def _asset_client(self) -> SearchClient:
        return SearchClient(
            endpoint=self._config.endpoint,
            index_name=self._config.asset_index_name,
            credential=self._credential,
        )

    async def _client_for(self, target: VectorTarget) -> SearchClient:
        if target == VectorTarget.ASSET:
            return await self._asset_client()
        return await self._client()

    async def search(
        self,
        embedding: list[float],
        top_k: int,
        filters: list[SearchFilter] | None,
        target: VectorTarget = VectorTarget.CORPUS,
    ) -> list[VectorHit]:
        allowed = _ASSET_ALLOWED_FIELDS if target == VectorTarget.ASSET else _ALLOWED_FIELDS
        filter_str = _build_odata_filter(filters, allowed)
        async with await self._client_for(target) as client:
            results = await client.search(
                search_text=None,
                vector_queries=[
                    {
                        "kind": "vector",
                        "vector": embedding,
                        "k": top_k,
                        "fields": "content_vector",
                    }
                ],
                filter=filter_str,
                select=["id", "payload"],
            )
            hits = []
            async for r in results:
                raw_score = min(max(r.get("@search.score", 0.0), 0.0), 1.0)
                raw_payload = r.get("payload")
                payload_dict = {}
                if raw_payload:
                    try:
                        payload_dict = json.loads(raw_payload)
                    except json.JSONDecodeError, TypeError:
                        payload_dict = {}
                hits.append(
                    VectorHit(
                        id=r["id"],
                        score=raw_score,
                        payload=payload_dict,
                    )
                )
            return hits

    async def upsert(
        self,
        items: list[VectorItem],
        target: VectorTarget = VectorTarget.CORPUS,
    ) -> int:
        docs = [
            {
                "id": item.id,
                "content_vector": item.vector,
                "payload": json.dumps(item.payload) if item.payload else "{}",
            }
            for item in items
        ]
        async with await self._client_for(target) as client:
            result = await client.merge_or_upload_documents(documents=docs)
            return sum(1 for r in result if r.succeeded)

    async def delete_asset_batch(self, batch_id: str) -> int:
        filter_str = _build_odata_filter(
            [SearchFilter(field="batch_id", value=batch_id)], _ASSET_ALLOWED_FIELDS
        )
        deleted = 0
        async with await self._asset_client() as client:
            while True:
                results = await client.search(
                    search_text="*",
                    filter=filter_str,
                    select=["id"],
                    top=_DELETE_PAGE_SIZE,
                )
                page = [{"id": r["id"]} async for r in results]
                if not page:
                    break
                await client.delete_documents(documents=page)
                deleted += len(page)
        return deleted

    async def embed(self, texts: list[str]) -> list[list[float]]:
        return await self._embedder.embed(texts)

    async def ensure_index(self) -> None:
        await self._ensure(self._config.index_name, _build_corpus_index)

    async def ensure_asset_store(self) -> None:
        await self._ensure(self._config.asset_index_name, _build_asset_index)

    async def _ensure(self, index_name: str, builder) -> None:
        async with SearchIndexClient(
            endpoint=self._config.endpoint, credential=self._credential
        ) as index_client:
            try:
                await index_client.get_index(index_name)
            except ResourceNotFoundError:
                await index_client.create_index(builder(index_name, self._config.dimensions))


def _vector_search() -> VectorSearch:
    return VectorSearch(
        profiles=[
            VectorSearchProfile(
                name="cortexa-hnsw",
                algorithm_configuration_name="cortexa-hnsw-algo",
            )
        ],
        algorithms=[HnswAlgorithmConfiguration(name="cortexa-hnsw-algo")],
    )


def _vector_field(dimensions: int) -> SearchField:
    return SearchField(
        name="content_vector",
        type=SearchFieldDataType.Collection(SearchFieldDataType.Single),
        searchable=True,
        vector_search_dimensions=dimensions,
        vector_search_profile_name="cortexa-hnsw",
    )


def _key_field() -> SimpleField:
    return SimpleField(
        name="id",
        type=SearchFieldDataType.String,
        key=True,
        filterable=True,
    )


def _build_corpus_index(name: str, dimensions: int) -> SearchIndex:
    return SearchIndex(
        name=name,
        fields=[
            _key_field(),
            _vector_field(dimensions),
            SimpleField(name="payload", type=SearchFieldDataType.String),
            SimpleField(name="source", type=SearchFieldDataType.String, filterable=True),
            SimpleField(name="doc_type", type=SearchFieldDataType.String, filterable=True),
            SimpleField(name="batch_id", type=SearchFieldDataType.String, filterable=True),
            SimpleField(name="chunk_index", type=SearchFieldDataType.String, filterable=True),
        ],
        vector_search=_vector_search(),
    )


def _build_asset_index(name: str, dimensions: int) -> SearchIndex:
    return SearchIndex(
        name=name,
        fields=[
            _key_field(),
            _vector_field(dimensions),
            SimpleField(name="payload", type=SearchFieldDataType.String),
            SimpleField(name="batch_id", type=SearchFieldDataType.String, filterable=True),
            SimpleField(name="document_id", type=SearchFieldDataType.String, filterable=True),
            SimpleField(name="chunk_id", type=SearchFieldDataType.String, filterable=True),
        ],
        vector_search=_vector_search(),
    )


_ALLOWED_FIELDS = frozenset({"id", "source", "doc_type", "batch_id", "chunk_index"})
_ASSET_ALLOWED_FIELDS = frozenset({"id", "batch_id", "document_id", "chunk_id"})


def _build_odata_filter(
    filters: list[SearchFilter] | None,
    allowed: frozenset[str] = _ALLOWED_FIELDS,
) -> str | None:
    if not filters:
        return None
    clauses = []
    for f in filters:
        if f.field not in allowed:
            raise ValueError(f"Unknown filter field: {f.field!r}")
        escaped = f.value.replace("'", "''")
        clauses.append(f"{f.field} eq '{escaped}'")
    return " and ".join(clauses)
