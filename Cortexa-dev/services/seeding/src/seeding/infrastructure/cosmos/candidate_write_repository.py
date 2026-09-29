from azure.cosmos.aio import ContainerProxy
from azure.cosmos.exceptions import CosmosHttpResponseError

from seeding.domain.errors.seeding_errors import StorageWriteError


class CosmosCandidateWriteRepository:
    def __init__(self, container: ContainerProxy) -> None:
        self._container = container

    async def upsert_many(self, candidates: list[dict]) -> None:
        for candidate in candidates:
            await self._upsert_one(candidate)

    async def _upsert_one(self, candidate: dict) -> None:
        try:
            await self._container.upsert_item(candidate)
        except CosmosHttpResponseError as exc:
            raise StorageWriteError(str(exc)) from exc
