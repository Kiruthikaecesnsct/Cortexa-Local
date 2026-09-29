from azure.cosmos.aio import CosmosClient
from azure.identity.aio import DefaultAzureCredential

from seeding.infrastructure.config.settings import SeedingSettings


def get_cosmos_client(
    settings: SeedingSettings, credential: DefaultAzureCredential
) -> CosmosClient:
    return CosmosClient(url=settings.cosmos_uri, credential=credential)
