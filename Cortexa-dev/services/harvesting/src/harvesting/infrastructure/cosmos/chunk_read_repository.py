from azure.cosmos.aio import ContainerProxy
from azure.cosmos.exceptions import CosmosHttpResponseError

from harvesting.domain.errors.harvesting_errors import StorageWriteError


class CosmosChunkReadRepository:
    def __init__(self, container: ContainerProxy) -> None:
        self._container = container

    async def get_by_document_order(
        self, batch_id: str, document_id: str, order_index: int
    ) -> dict | None:
        query = (
            "SELECT TOP 1 * FROM c WHERE c.document_id = @document_id "
            "AND c.order_index = @order_index"
        )
        parameters = [
            {"name": "@document_id", "value": document_id},
            {"name": "@order_index", "value": order_index},
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

        return items[0] if items else None
