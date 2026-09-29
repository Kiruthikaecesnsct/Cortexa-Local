from azure.cosmos.aio import CosmosClient
from azure.identity.aio import DefaultAzureCredential

from seeding.infrastructure.config.settings import SeedingSettings


def get_cosmos_client(
    settings: SeedingSettings, credential: DefaultAzureCredential
) -> CosmosClient:
    # A configured key (local/emulator auth) takes precedence over managed identity.
    effective_credential: DefaultAzureCredential | str = settings.cosmos_key or credential
    return CosmosClient(url=settings.cosmos_uri, credential=effective_credential)
