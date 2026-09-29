from azure.cosmos.aio import CosmosClient
from azure.identity.aio import DefaultAzureCredential

from evidence.infrastructure.config.settings import EvidenceSettings


def get_cosmos_client(
    settings: EvidenceSettings, credential: DefaultAzureCredential
) -> CosmosClient:
    return CosmosClient(url=settings.cosmos_uri, credential=credential)
