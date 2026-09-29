from ..domain.backend import VectorBackend
from ..domain.models import SearchFilter, VectorHit, VectorItem, VectorTarget


class VectorRouter:
    def __init__(self, backend: VectorBackend) -> None:
        self._backend = backend

    async def search(
        self,
        embedding: list[float],
        top_k: int,
        filters: list[SearchFilter] | None = None,
        target: VectorTarget = VectorTarget.CORPUS,
    ) -> list[VectorHit]:
        return await self._backend.search(embedding, top_k, filters, target=target)

    async def upsert(
        self,
        items: list[VectorItem],
        target: VectorTarget = VectorTarget.CORPUS,
    ) -> int:
        return await self._backend.upsert(items, target=target)

    async def delete_asset_batch(self, batch_id: str) -> int:
        return await self._backend.delete_asset_batch(batch_id)

    async def embed(self, texts: list[str]) -> list[list[float]]:
        return await self._backend.embed(texts)
