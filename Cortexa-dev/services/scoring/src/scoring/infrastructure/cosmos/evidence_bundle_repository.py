from azure.cosmos.aio import ContainerProxy
from pydantic import BaseModel

from scoring.domain.models.evidence_bundle import EvidenceBundle


class EvidenceBundleRecord(BaseModel):
    id: str
    batch_id: str
    candidate_id: str
    hits: list[dict]
    sources_used: list[str]
    source_flags: dict[str, bool]


class CosmosEvidenceBundleRepository:
    def __init__(self, container: ContainerProxy) -> None:
        self._container = container

    async def get_domain_bundle(self, candidate_id: str, batch_id: str) -> EvidenceBundle | None:
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
        return EvidenceBundle.model_validate(results[0])

    async def get_by_candidate_id(
        self, candidate_id: str, batch_id: str
    ) -> EvidenceBundleRecord | None:
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
        return EvidenceBundleRecord.model_validate(results[0])
