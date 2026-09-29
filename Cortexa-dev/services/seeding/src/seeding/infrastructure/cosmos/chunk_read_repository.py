from azure.cosmos.aio import ContainerProxy
from azure.cosmos.exceptions import CosmosHttpResponseError

from seeding.domain.errors.seeding_errors import StorageWriteError


class CosmosChunkReadRepository:
    def __init__(self, container: ContainerProxy) -> None:
        self._container = container

    async def get_range(
        self, batch_id: str, document_id: str, order_start: int, order_end: int
    ) -> list[dict]:
        query = (
            "SELECT * FROM c "
            "WHERE c.batch_id = @batch_id "
            "AND c.document_id = @document_id "
            "AND c.order_index >= @order_start "
            "AND c.order_index < @order_end "
            "ORDER BY c.order_index ASC"
        )
        parameters = [
            {"name": "@batch_id", "value": batch_id},
            {"name": "@document_id", "value": document_id},
            {"name": "@order_start", "value": order_start},
            {"name": "@order_end", "value": order_end},
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
