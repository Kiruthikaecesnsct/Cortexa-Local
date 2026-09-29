from azure.cosmos.aio import ContainerProxy

from ingestion.domain.models.chunk import Chunk


class ChunkRepository:
    def __init__(self, container: ContainerProxy) -> None:
        self._container = container

    async def save_many(
        self, batch_id: str, document_id: str, chunks_with_ids: list[tuple[str, Chunk]]
    ) -> None:
        for chunk_id, chunk in chunks_with_ids:
            item = {"id": chunk_id, "batch_id": batch_id, "document_id": document_id}
            item.update(chunk.model_dump())
            await self._container.upsert_item(item)

    async def delete_many(self, batch_id: str, chunk_ids: list[str]) -> None:
        for chunk_id in chunk_ids:
            await self._container.delete_item(item=chunk_id, partition_key=batch_id)

    async def exists(self, batch_id: str, document_id: str) -> bool:
        query = "SELECT VALUE COUNT(1) FROM c WHERE c.document_id = @doc_id"
        params: list[dict] = [{"name": "@doc_id", "value": document_id}]
        items = self._container.query_items(
            query=query,
            parameters=params,
            partition_key=batch_id,
        )
        async for count in items:
            return count > 0
        return False
