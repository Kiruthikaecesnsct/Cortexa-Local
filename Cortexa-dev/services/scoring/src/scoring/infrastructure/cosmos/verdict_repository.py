from azure.cosmos.aio import ContainerProxy
from azure.cosmos.exceptions import CosmosHttpResponseError, CosmosResourceNotFoundError

from scoring.domain.errors.storage_errors import StorageWriteError, VerdictNotFoundError
from scoring.domain.models.stored_verdict import StoredVerdict


class CosmosVerdictRepository:
    def __init__(self, container: ContainerProxy) -> None:
        self._container = container

    async def save(self, verdict: StoredVerdict) -> None:
        item_dict = verdict.model_dump(mode="json")
        try:
            await self._container.upsert_item(item_dict)
        except CosmosHttpResponseError as exc:
            raise StorageWriteError(str(exc)) from exc

    async def find_by_candidate(self, candidate_id: str, batch_id: str) -> StoredVerdict | None:
        item_id = f"{batch_id}:{candidate_id}"
        try:
            item = await self._container.read_item(item=item_id, partition_key=batch_id)
            return StoredVerdict.model_validate(item)
        except CosmosResourceNotFoundError:
            return None

    async def get_by_candidate(self, candidate_id: str) -> StoredVerdict:
        query = "SELECT * FROM c WHERE c.candidate_id = @candidate_id"
        params = [{"name": "@candidate_id", "value": candidate_id}]
        try:
            items = self._container.query_items(
                query=query,
                parameters=params,
                enable_cross_partition_query=True,
            )
            results = [item async for item in items]
            if not results:
                raise VerdictNotFoundError(candidate_id)
            return StoredVerdict.model_validate(results[0])
        except CosmosResourceNotFoundError as exc:
            raise VerdictNotFoundError(candidate_id) from exc
