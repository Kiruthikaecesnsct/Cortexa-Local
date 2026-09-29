from uuid import UUID, uuid5

from azure.cosmos.aio import ContainerProxy
from azure.cosmos.exceptions import CosmosHttpResponseError

from seeding.domain.errors.seeding_errors import StorageWriteError
from seeding.domain.models.scratchpad import IdeationScratchpad

SCRATCHPAD_NS = UUID("d3f5b8c1-6a2e-5f4d-9b0c-1e7a3c5d9f2b")
_SCRATCHPAD_TYPE = "ideation_scratchpad"
_BY_DOCUMENT_QUERY = (
    "SELECT * FROM c WHERE c.batch_id = @batch_id "
    "AND c.document_id = @document_id AND c.type = @type "
    "AND c.schema_version = @schema_version"
)


def scratchpad_id(batch_id: str, document_id: str, schema_version: str) -> str:
    return str(uuid5(SCRATCHPAD_NS, f"{batch_id}:{document_id}:{schema_version}:scratchpad"))


def _by_document_params(batch_id: str, document_id: str, schema_version: str) -> list:
    return [
        {"name": "@batch_id", "value": batch_id},
        {"name": "@document_id", "value": document_id},
        {"name": "@type", "value": _SCRATCHPAD_TYPE},
        {"name": "@schema_version", "value": schema_version},
    ]


class ScratchpadRepository:
    def __init__(self, container: ContainerProxy) -> None:
        self._container = container

    async def save(self, scratchpad: IdeationScratchpad) -> None:
        document = scratchpad.model_dump(mode="json")
        document["id"] = scratchpad.id
        document["batch_id"] = scratchpad.batch_id
        try:
            await self._container.upsert_item(document)
        except CosmosHttpResponseError as exc:
            raise StorageWriteError(str(exc)) from exc

    async def get(
        self, batch_id: str, document_id: str, schema_version: str
    ) -> IdeationScratchpad | None:
        parameters = _by_document_params(batch_id, document_id, schema_version)
        try:
            items = [
                item
                async for item in self._container.query_items(
                    query=_BY_DOCUMENT_QUERY, parameters=parameters, partition_key=batch_id
                )
            ]
        except CosmosHttpResponseError as exc:
            raise StorageWriteError(str(exc)) from exc
        if not items:
            return None
        return IdeationScratchpad.model_validate(items[0])
