from azure.cosmos.aio import CosmosClient
from azure.identity.aio import DefaultAzureCredential

from extraction.infrastructure.config.settings import ExtractionSettings


def get_cosmos_client(
    settings: ExtractionSettings, credential: DefaultAzureCredential
) -> CosmosClient:
    if not settings.cosmos_uri:
        raise ValueError("cosmos_uri is not configured.")
    return CosmosClient(url=settings.cosmos_uri, credential=credential)
