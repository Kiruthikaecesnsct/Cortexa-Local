from azure.cosmos.aio import ContainerProxy
from azure.cosmos.exceptions import CosmosHttpResponseError

from harvesting.domain.errors.harvesting_errors import StorageWriteError


class CosmosCandidateReadRepository:
    def __init__(self, container: ContainerProxy) -> None:
        self._container = container

    async def get_by_batch(self, batch_id: str) -> list[dict]:
        query = (
            "SELECT * FROM c WHERE c.batch_id = @batch_id "
            "AND (NOT IS_DEFINED(c.engine) OR c.engine != 'seeding')"
        )
        parameters = [{"name": "@batch_id", "value": batch_id}]
        try:
            items = [
                item
                async for item in self._container.query_items(
                    query=query, parameters=parameters, partition_key=batch_id
                )
            ]
        except CosmosHttpResponseError as exc:
            raise StorageWriteError(str(exc)) from exc

        return items
