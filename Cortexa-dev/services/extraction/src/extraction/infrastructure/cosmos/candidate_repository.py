from azure.cosmos.aio import ContainerProxy

from extraction.domain.errors.storage_errors import PartialSaveError
from extraction.domain.models.invention_candidate import InventionCandidate


class CandidateRepository:
    def __init__(self, container: ContainerProxy) -> None:
        self._container = container

    async def save_many(self, candidates: list[InventionCandidate]) -> list[str]:
        saved_ids: list[str] = []
        for candidate in candidates:
            item = {"id": candidate.id, "document_id": candidate.document_id}
            item.update(candidate.model_dump(mode="json"))
            try:
                await self._container.upsert_item(item)
                saved_ids.append(candidate.id)
            except Exception as exc:
                raise PartialSaveError(str(exc), saved_ids) from exc
        return saved_ids

    async def delete_many(self, batch_id: str, candidate_ids: list[str]) -> None:
        for candidate_id in candidate_ids:
            await self._container.delete_item(item=candidate_id, partition_key=batch_id)

    async def exists_for_unit(self, batch_id: str, document_id: str, unit_index: int) -> bool:
        query = (
            "SELECT VALUE COUNT(1) FROM c "
            "WHERE c.document_id = @doc_id AND c.extraction_unit_index = @unit_index"
        )
        params: list[dict] = [
            {"name": "@doc_id", "value": document_id},
            {"name": "@unit_index", "value": unit_index},
        ]
        items = self._container.query_items(
            query=query,
            parameters=params,
            partition_key=batch_id,
        )
        async for count in items:
            return count > 0
        return False

    async def get_candidate_ids_for_unit(
        self, batch_id: str, document_id: str, unit_index: int
    ) -> list[str]:
        query = (
            "SELECT VALUE c.id FROM c "
            "WHERE c.document_id = @doc_id AND c.extraction_unit_index = @unit_index"
        )
        params: list[dict] = [
            {"name": "@doc_id", "value": document_id},
            {"name": "@unit_index", "value": unit_index},
        ]
        items = self._container.query_items(
            query=query,
            parameters=params,
            partition_key=batch_id,
        )
        return [candidate_id async for candidate_id in items]
