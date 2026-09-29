from azure.cosmos.aio import ContainerProxy

from seeding.domain.errors.seeding_errors import SeedingError
from seeding.domain.models.seeding_result import SeedingResult


class SeedingResultNotFoundError(SeedingError):
    def __init__(self, batch_id: str) -> None:
        super().__init__(
            f"Seeding result not found for batch {batch_id}", code="SEEDING_RESULT_NOT_FOUND"
        )


class SeedingRepository:
    def __init__(self, container: ContainerProxy) -> None:
        self._container = container

    async def save(self, result: SeedingResult) -> None:
        document = {"id": result.id, "batch_id": result.batch_id}
        document.update(result.model_dump(mode="json"))
        await self._container.upsert_item(document)

    async def get_by_batch(self, batch_id: str) -> SeedingResult:
        query = "SELECT * FROM c WHERE c.batch_id = @batch_id"
        parameters = [{"name": "@batch_id", "value": batch_id}]
        items = []
        async for item in self._container.query_items(
            query=query, parameters=parameters, partition_key=batch_id
        ):
            items.append(item)

        if not items:
            raise SeedingResultNotFoundError(batch_id)

        return SeedingResult.model_validate(items[0])
