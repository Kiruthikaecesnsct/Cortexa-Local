import asyncio
import logging
from typing import Any

logger = logging.getLogger(__name__)

_SKIP_MSG = "Cosmos direct check skipped: %s"


def _cosmos_client_class() -> Any | None:
    try:
        from azure.cosmos import CosmosClient  # noqa: PLC0415

        return CosmosClient
    except ImportError:
        return None


def _query_items(connection_string: str, container_name: str, batch_id: str) -> list[dict]:
    client_class = _cosmos_client_class()
    client = client_class.from_connection_string(connection_string)
    db = client.get_database_client("cortexa-pipeline")
    container = db.get_container_client(container_name)
    return list(
        container.query_items(
            query="SELECT * FROM c WHERE c.batch_id = @batch_id",
            parameters=[{"name": "@batch_id", "value": batch_id}],
            enable_cross_partition_query=True,
        )
    )


async def read_cosmos_container(
    batch_id: str,
    container_name: str,
    connection_string: str | None,
) -> list[dict] | None:
    if not connection_string:
        logger.info(_SKIP_MSG, "no connection string configured")
        return None

    if _cosmos_client_class() is None:
        logger.info(_SKIP_MSG, "azure-cosmos package not installed")
        return None

    return await asyncio.to_thread(_query_items, connection_string, container_name, batch_id)
