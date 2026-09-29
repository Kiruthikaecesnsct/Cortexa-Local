from azure.core.exceptions import ServiceRequestError
from azure.cosmos.aio import ContainerProxy
from azure.cosmos.exceptions import CosmosHttpResponseError

from evidence.domain.errors.evidence_errors import StorageWriteError
from evidence.domain.models.evidence_bundle import EvidenceBundle

_FIND_QUERY = "SELECT * FROM c WHERE c.document_id = @doc_id AND c.candidate_id = @cand_id"


class CosmosEvidenceBundleRepository:
    def __init__(self, container: ContainerProxy) -> None:
        self._container = container

    async def save(self, bundle: EvidenceBundle) -> None:
        item_dict = bundle.model_dump(mode="json")
        try:
            await self._container.upsert_item(item_dict)
        except (CosmosHttpResponseError, ServiceRequestError, TimeoutError) as exc:
            raise StorageWriteError(str(exc)) from exc

    async def find_for_candidate(
        self, batch_id: str, document_id: str, candidate_id: str
    ) -> EvidenceBundle | None:
        parameters = [
            {"name": "@doc_id", "value": document_id},
            {"name": "@cand_id", "value": candidate_id},
        ]
        try:
            async for item in self._container.query_items(
                query=_FIND_QUERY,
                parameters=parameters,
                partition_key=batch_id,
            ):
                return EvidenceBundle.model_validate(item)
            return None
        except (CosmosHttpResponseError, ServiceRequestError, TimeoutError) as exc:
            raise StorageWriteError(str(exc)) from exc
