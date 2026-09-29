from azure.cosmos.aio import ContainerProxy
from azure.cosmos.exceptions import CosmosHttpResponseError

from seeding.domain.errors.seeding_errors import SeedingReportNotFoundError, StorageWriteError
from seeding.domain.models.seeding_result import SeedingResult

_ENGINE_FILTER = "seeding"


class SeedingReportRepository:
    def __init__(self, container: ContainerProxy) -> None:
        self._container = container

    async def save(self, result: SeedingResult) -> None:
        document = result.model_dump(mode="json")
        document["id"] = result.id
        document["batch_id"] = result.batch_id
        try:
            await self._container.upsert_item(document)
        except CosmosHttpResponseError as exc:
            raise StorageWriteError(str(exc)) from exc

    async def get_by_batch(self, batch_id: str) -> SeedingResult:
        query = "SELECT * FROM c WHERE c.batch_id = @b AND c.engine = @engine"
        parameters = [
            {"name": "@b", "value": batch_id},
            {"name": "@engine", "value": _ENGINE_FILTER},
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

        if not items:
            raise SeedingReportNotFoundError(batch_id)

        return SeedingResult.model_validate(items[0])
