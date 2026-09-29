from uuid import UUID, uuid5

from azure.cosmos.aio import ContainerProxy
from azure.cosmos.exceptions import CosmosHttpResponseError

from seeding.domain.errors.seeding_errors import StorageWriteError
from seeding.domain.models.landscape import PriorArtLandscape

LANDSCAPE_NS = UUID("a7c9e2d4-3b1f-5c6a-8e0d-2f4b6a8c0e1d")
_LANDSCAPE_TYPE = "prior_art_landscape"
_BY_DOCUMENT_QUERY = (
    "SELECT * FROM c WHERE c.batch_id = @batch_id "
    "AND c.document_id = @document_id AND c.type = @type "
    "AND c.schema_version = @schema_version"
)


def landscape_id(batch_id: str, document_id: str, schema_version: str) -> str:
    return str(uuid5(LANDSCAPE_NS, f"{batch_id}:{document_id}:{schema_version}:landscape"))


def _by_document_params(batch_id: str, document_id: str, schema_version: str) -> list:
    return [
        {"name": "@batch_id", "value": batch_id},
        {"name": "@document_id", "value": document_id},
        {"name": "@type", "value": _LANDSCAPE_TYPE},
        {"name": "@schema_version", "value": schema_version},
    ]


class LandscapeRepository:
    def __init__(self, container: ContainerProxy) -> None:
        self._container = container

    async def save_landscape(self, landscape: PriorArtLandscape) -> None:
        document = landscape.model_dump(mode="json")
        document["id"] = landscape.id
        document["batch_id"] = landscape.batch_id
        try:
            await self._container.upsert_item(document)
        except CosmosHttpResponseError as exc:
            raise StorageWriteError(str(exc)) from exc

    async def get_landscape(
        self, batch_id: str, document_id: str, schema_version: str
    ) -> PriorArtLandscape | None:
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
        return PriorArtLandscape.model_validate(items[0])
