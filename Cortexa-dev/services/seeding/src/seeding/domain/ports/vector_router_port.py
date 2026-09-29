from typing import Protocol


class VectorRouterPort(Protocol):
    async def embed(self, texts: list[str]) -> list[list[float]]: ...

    async def upsert(self, items: list[dict], target: str = "asset") -> int: ...

    async def search(
        self,
        embedding: list[float],
        top_k: int,
        target: str,
        filters: list[dict] | None = None,
    ) -> list[dict]: ...
