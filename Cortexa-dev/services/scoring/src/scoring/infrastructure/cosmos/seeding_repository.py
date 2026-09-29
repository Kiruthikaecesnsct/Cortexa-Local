from azure.cosmos.aio import ContainerProxy
from pydantic import BaseModel


class ClaimSeedSetRecord(BaseModel):
    candidate_id: str
    batch_id: str
    independent_claims: list[dict]
    dependent_claims: list[dict]


class CosmosClaimSeedRepository:
    def __init__(self, container: ContainerProxy) -> None:
        self._container = container

    async def get_by_candidate_id(
        self, candidate_id: str, batch_id: str
    ) -> ClaimSeedSetRecord | None:
        query = "SELECT * FROM c WHERE c.candidate_id = @candidate_id AND c.batch_id = @batch_id"
        params = [
            {"name": "@candidate_id", "value": candidate_id},
            {"name": "@batch_id", "value": batch_id},
        ]
        items = self._container.query_items(
            query=query,
            parameters=params,
            partition_key=batch_id,
        )
        results = [item async for item in items]
        if not results:
            return None
        return ClaimSeedSetRecord.model_validate(results[0])
