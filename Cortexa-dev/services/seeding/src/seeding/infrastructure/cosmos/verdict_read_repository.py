from azure.cosmos.aio import ContainerProxy
from azure.cosmos.exceptions import CosmosHttpResponseError

from seeding.domain.errors.seeding_errors import StorageWriteError


class CosmosVerdictReadRepository:
    def __init__(self, container: ContainerProxy) -> None:
        self._container = container

    async def get_for_candidates(self, batch_id: str, candidate_ids: set[str]) -> list[dict]:
        if not candidate_ids:
            return []
        query = "SELECT * FROM c WHERE c.batch_id = @batch_id"
        parameters = [{"name": "@batch_id", "value": batch_id}]
        try:
            items = [
                item
                async for item in self._container.query_items(
                    query=query, parameters=parameters, partition_key=batch_id
                )
                if item.get("candidate_id") in candidate_ids
            ]
        except CosmosHttpResponseError as exc:
            raise StorageWriteError(str(exc)) from exc

        return items
