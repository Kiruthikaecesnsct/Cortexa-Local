from azure.cosmos.aio import ContainerProxy
from azure.cosmos.exceptions import CosmosResourceNotFoundError

from ingestion.domain.errors.provenance_errors import ChunkNotFoundError
from ingestion.domain.models.provenance_entry import ProvenanceEntry
from ingestion.domain.models.provenance_map import ProvenanceMap


class ProvenanceRepository:
    def __init__(self, container: ContainerProxy) -> None:
        self._container = container

    async def save(self, provenance_map: ProvenanceMap) -> None:
        for entry in provenance_map.entries:
            document = {"id": entry.chunk_id, "batch_id": provenance_map.batch_id}
            document.update(entry.model_dump())
            await self._container.upsert_item(document)

    async def get_entry(self, batch_id: str, chunk_id: str) -> ProvenanceEntry:
        try:
            item = await self._container.read_item(item=chunk_id, partition_key=batch_id)
        except CosmosResourceNotFoundError:
            raise ChunkNotFoundError(chunk_id)
        return ProvenanceEntry.model_validate(item)

    async def delete_map(self, batch_id: str, chunk_ids: list[str]) -> None:
        for chunk_id in chunk_ids:
            await self._container.delete_item(item=chunk_id, partition_key=batch_id)
