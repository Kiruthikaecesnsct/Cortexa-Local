from azure.cosmos.aio import ContainerProxy
from azure.cosmos.exceptions import CosmosResourceNotFoundError

from evidence.domain.errors.evidence_errors import CandidateNotFoundError
from evidence.domain.repositories.storage_protocols import CandidateRecord


class CosmosCandidateRepository:
    def __init__(self, container: ContainerProxy) -> None:
        self._container = container

    async def get_by_candidate_id(self, candidate_id: str, batch_id: str) -> CandidateRecord:
        try:
            item = await self._container.read_item(item=candidate_id, partition_key=batch_id)
            return CandidateRecord.model_validate(item)
        except CosmosResourceNotFoundError as exc:
            raise CandidateNotFoundError(candidate_id) from exc
