from typing import Protocol, runtime_checkable

from .models import SearchFilter, VectorHit, VectorItem, VectorTarget


@runtime_checkable
class VectorBackend(Protocol):
    async def search(
        self,
        embedding: list[float],
        top_k: int,
        filters: list[SearchFilter] | None,
        target: VectorTarget = VectorTarget.CORPUS,
    ) -> list[VectorHit]: ...

    async def upsert(
        self,
        items: list[VectorItem],
        target: VectorTarget = VectorTarget.CORPUS,
    ) -> int: ...

    async def delete_asset_batch(self, batch_id: str) -> int: ...

    async def embed(self, texts: list[str]) -> list[list[float]]: ...
