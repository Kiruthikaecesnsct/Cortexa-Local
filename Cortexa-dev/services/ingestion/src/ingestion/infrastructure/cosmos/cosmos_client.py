from azure.cosmos.aio import CosmosClient
from azure.identity.aio import DefaultAzureCredential

from ingestion.infrastructure.config.settings import IngestionSettings


def get_cosmos_client(
    settings: IngestionSettings, credential: DefaultAzureCredential
) -> CosmosClient:
    if not settings.cosmos_uri:
        raise ValueError("cosmos_uri is not configured. Set the COSMOS_URI environment variable.")
    # A configured key (local/emulator auth) takes precedence over managed identity.
    effective_credential: DefaultAzureCredential | str = settings.cosmos_key or credential
    return CosmosClient(url=settings.cosmos_uri, credential=effective_credential)
