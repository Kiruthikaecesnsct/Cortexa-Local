from azure.cosmos.aio import CosmosClient
from azure.identity.aio import DefaultAzureCredential

from scoring.infrastructure.config.settings import ScoringSettings


def get_cosmos_client(
    settings: ScoringSettings, credential: DefaultAzureCredential
) -> CosmosClient:
    # A configured key (local/emulator auth) takes precedence over managed identity.
    effective_credential: DefaultAzureCredential | str = settings.cosmos_key or credential
    return CosmosClient(url=settings.cosmos_uri, credential=effective_credential)
