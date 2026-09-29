from azure.cosmos.aio import ContainerProxy
from azure.cosmos.exceptions import CosmosResourceNotFoundError

from ingestion.domain.models.document import Document
from ingestion.domain.models.document_ref import DocumentRef


class DocumentRepository:
    def __init__(self, container: ContainerProxy) -> None:
        self._container = container

    async def save(self, document: Document) -> None:
        item = {"id": document.id, "batch_id": document.batch_id}
        item.update(document.model_dump(mode="json"))
        await self._container.upsert_item(item)

    async def delete(self, document_id: str, batch_id: str) -> None:
        await self._container.delete_item(item=document_id, partition_key=batch_id)

    async def get(self, document_id: str, batch_id: str) -> DocumentRef | None:
        try:
            item = await self._container.read_item(item=document_id, partition_key=batch_id)
        except CosmosResourceNotFoundError:
            return None
        return DocumentRef.model_validate(item)

    async def mark_completed(self, document_id: str, batch_id: str) -> None:
        try:
            item = await self._container.read_item(item=document_id, partition_key=batch_id)
        except CosmosResourceNotFoundError:
            return
        item["status"] = "completed"
        await self._container.upsert_item(item)
