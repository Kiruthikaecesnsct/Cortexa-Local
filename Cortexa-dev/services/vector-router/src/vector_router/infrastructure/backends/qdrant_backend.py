from qdrant_client import AsyncQdrantClient
from qdrant_client.models import (
    Distance,
    FieldCondition,
    Filter,
    FilterSelector,
    MatchValue,
    PointStruct,
    VectorParams,
)

from ...domain.models import SearchFilter, VectorHit, VectorItem, VectorTarget
from ..embedding.embedder import Embedder

_ASSET_BATCH_FIELD = "batch_id"


class QdrantBackend:
    def __init__(
        self,
        url: str,
        collection_name: str,
        asset_collection_name: str,
        dimensions: int,
        embedder: Embedder,
    ) -> None:
        self._client = AsyncQdrantClient(url=url)
        self._collection_name = collection_name
        self._asset_collection_name = asset_collection_name
        self._dimensions = dimensions
        self._embedder = embedder

    def _collection_for(self, target: VectorTarget) -> str:
        if target == VectorTarget.ASSET:
            return self._asset_collection_name
        return self._collection_name

    async def search(
        self,
        embedding: list[float],
        top_k: int,
        filters: list[SearchFilter] | None,
        target: VectorTarget = VectorTarget.CORPUS,
    ) -> list[VectorHit]:
        qdrant_filter = _build_qdrant_filter(filters)
        results = await self._client.search(
            collection_name=self._collection_for(target),
            query_vector=embedding,
            query_filter=qdrant_filter,
            limit=top_k,
            with_payload=True,
        )
        return [
            VectorHit(
                id=str(r.id),
                score=(r.score + 1.0) / 2.0,
                payload=r.payload or {},
            )
            for r in results
        ]

    async def upsert(
        self,
        items: list[VectorItem],
        target: VectorTarget = VectorTarget.CORPUS,
    ) -> int:
        points = [
            PointStruct(id=item.id, vector=item.vector, payload=item.payload) for item in items
        ]
        await self._client.upsert(collection_name=self._collection_for(target), points=points)
        return len(points)

    async def delete_asset_batch(self, batch_id: str) -> int:
        batch_filter = Filter(
            must=[FieldCondition(key=_ASSET_BATCH_FIELD, match=MatchValue(value=batch_id))]
        )
        count_result = await self._client.count(
            collection_name=self._asset_collection_name,
            count_filter=batch_filter,
            exact=True,
        )
        deleted = count_result.count
        if deleted:
            await self._client.delete(
                collection_name=self._asset_collection_name,
                points_selector=FilterSelector(filter=batch_filter),
            )
        return deleted

    async def embed(self, texts: list[str]) -> list[list[float]]:
        return await self._embedder.embed(texts)

    async def ensure_asset_store(self) -> None:
        if await self._client.collection_exists(self._asset_collection_name):
            return
        await self._client.create_collection(
            collection_name=self._asset_collection_name,
            vectors_config=VectorParams(size=self._dimensions, distance=Distance.COSINE),
        )


def _build_qdrant_filter(filters: list[SearchFilter] | None) -> Filter | None:
    if not filters:
        return None
    conditions = [FieldCondition(key=f.field, match=MatchValue(value=f.value)) for f in filters]
    return Filter(must=conditions)
