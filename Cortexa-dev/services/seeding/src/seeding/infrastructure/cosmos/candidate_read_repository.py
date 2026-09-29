from azure.cosmos.aio import ContainerProxy
from azure.cosmos.exceptions import CosmosHttpResponseError

from seeding.domain.errors.seeding_errors import StorageWriteError

_SEEDED_ENGINE = "seeding"


class CosmosCandidateReadRepository:
    def __init__(self, container: ContainerProxy) -> None:
        self._container = container

    async def get_by_batch(self, batch_id: str) -> list[dict]:
        query = (
            "SELECT * FROM c WHERE c.batch_id = @batch_id "
            "AND (NOT IS_DEFINED(c.engine) OR c.engine != @engine)"
        )
        return await self._query(batch_id, query)

    async def get_seeded_by_batch(self, batch_id: str) -> list[dict]:
        query = "SELECT * FROM c WHERE c.batch_id = @batch_id AND c.engine = @engine"
        return await self._query(batch_id, query)

    async def _query(self, batch_id: str, query: str) -> list[dict]:
        parameters = [
            {"name": "@batch_id", "value": batch_id},
            {"name": "@engine", "value": _SEEDED_ENGINE},
        ]
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
