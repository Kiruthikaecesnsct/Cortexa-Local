from azure.cosmos.aio import ContainerProxy
from azure.cosmos.exceptions import CosmosHttpResponseError

from harvesting.domain.errors.harvesting_errors import StorageWriteError
from harvesting.domain.models.maturity_result import MaturityResult


class CosmosMaturityRepository:
    def __init__(self, container: ContainerProxy) -> None:
        self._container = container

    async def save(self, result: MaturityResult) -> None:
        item_dict = result.model_dump(mode="json")
        try:
            await self._container.upsert_item(item_dict)
        except CosmosHttpResponseError as exc:
            raise StorageWriteError(str(exc)) from exc
