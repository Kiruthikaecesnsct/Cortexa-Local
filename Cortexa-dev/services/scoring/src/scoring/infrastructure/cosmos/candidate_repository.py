from azure.cosmos.aio import ContainerProxy
from azure.cosmos.exceptions import CosmosResourceNotFoundError
from pydantic import BaseModel

from scoring.domain.errors.storage_errors import VerdictNotFoundError


class CandidateRecord(BaseModel):
    id: str
    batch_id: str
    document_id: str
    claim_text: str
    problem: str
    mechanism: str
    tech_field: str
    source_span: dict


class CosmosCandidateRepository:
    def __init__(self, container: ContainerProxy) -> None:
        self._container = container

    async def get_by_candidate_id(self, candidate_id: str, batch_id: str) -> CandidateRecord:
        try:
            item = await self._container.read_item(item=candidate_id, partition_key=batch_id)
            return CandidateRecord.model_validate(item)
        except CosmosResourceNotFoundError as exc:
            raise VerdictNotFoundError(candidate_id) from exc
