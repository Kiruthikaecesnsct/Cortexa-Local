from azure.cosmos.aio import ContainerProxy
from azure.cosmos.exceptions import CosmosHttpResponseError, CosmosResourceNotFoundError

from harvesting.domain.errors.harvesting_errors import StorageWriteError


class CosmosProvenanceEntryReadRepository:
    def __init__(self, container: ContainerProxy) -> None:
        self._container = container

    async def get(self, batch_id: str, chunk_id: str) -> dict | None:
        try:
            return await self._container.read_item(item=chunk_id, partition_key=batch_id)
        except CosmosResourceNotFoundError:
            return None
        except CosmosHttpResponseError as exc:
            raise StorageWriteError(str(exc)) from exc
