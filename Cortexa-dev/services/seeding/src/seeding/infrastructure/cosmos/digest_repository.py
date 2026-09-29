from azure.cosmos.aio import ContainerProxy
from azure.cosmos.exceptions import CosmosHttpResponseError

from seeding.domain.errors.seeding_errors import StorageWriteError
from seeding.domain.models.digest import (
    DigestReduceIntermediate,
    InventionContextBrief,
    SectionNote,
)

_SECTION_NOTE_TYPE = "digest_section_note"
_BRIEF_TYPE = "invention_context_brief"
_INTERMEDIATE_TYPE = "digest_reduce_intermediate"
_BY_DOCUMENT_QUERY = (
    "SELECT * FROM c WHERE c.batch_id = @batch_id "
    "AND c.document_id = @document_id AND c.type = @type "
    "AND c.prompt_version = @prompt_version"
)


def _by_document_params(
    batch_id: str, document_id: str, doc_type: str, prompt_version: str
) -> list:
    return [
        {"name": "@batch_id", "value": batch_id},
        {"name": "@document_id", "value": document_id},
        {"name": "@type", "value": doc_type},
        {"name": "@prompt_version", "value": prompt_version},
    ]


class DigestRepository:
    def __init__(self, container: ContainerProxy) -> None:
        self._container = container

    async def _upsert(self, document: dict) -> None:
        try:
            await self._container.upsert_item(document)
        except CosmosHttpResponseError as exc:
            raise StorageWriteError(str(exc)) from exc

    async def save_section_note(self, note: SectionNote) -> None:
        document = note.model_dump(mode="json")
        document["id"] = note.id
        document["batch_id"] = note.batch_id
        await self._upsert(document)

    async def save_brief(self, brief: InventionContextBrief) -> None:
        document = brief.model_dump(mode="json")
        document["id"] = brief.id
        document["batch_id"] = brief.batch_id
        await self._upsert(document)

    async def save_intermediate(self, item: DigestReduceIntermediate) -> None:
        document = item.model_dump(mode="json")
        document["id"] = item.id
        document["batch_id"] = item.batch_id
        await self._upsert(document)

    async def _query(self, query: str, parameters: list[dict], batch_id: str) -> list[dict]:
        try:
            return [
                item
                async for item in self._container.query_items(
                    query=query, parameters=parameters, partition_key=batch_id
                )
            ]
        except CosmosHttpResponseError as exc:
            raise StorageWriteError(str(exc)) from exc

    async def get_section_notes(
        self, batch_id: str, document_id: str, prompt_version: str
    ) -> list[SectionNote]:
        parameters = _by_document_params(batch_id, document_id, _SECTION_NOTE_TYPE, prompt_version)
        items = await self._query(_BY_DOCUMENT_QUERY, parameters, batch_id)
        return [SectionNote.model_validate(item) for item in items]

    async def get_intermediates(
        self, batch_id: str, document_id: str, prompt_version: str
    ) -> list[DigestReduceIntermediate]:
        parameters = _by_document_params(batch_id, document_id, _INTERMEDIATE_TYPE, prompt_version)
        items = await self._query(_BY_DOCUMENT_QUERY, parameters, batch_id)
        return [DigestReduceIntermediate.model_validate(item) for item in items]

    async def get_brief(
        self, batch_id: str, document_id: str, prompt_version: str
    ) -> InventionContextBrief | None:
        parameters = _by_document_params(batch_id, document_id, _BRIEF_TYPE, prompt_version)
        items = await self._query(_BY_DOCUMENT_QUERY, parameters, batch_id)
        if not items:
            return None
        return InventionContextBrief.model_validate(items[0])
