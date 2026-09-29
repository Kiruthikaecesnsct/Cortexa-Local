from azure.cosmos.aio import CosmosClient
from azure.identity.aio import DefaultAzureCredential

from harvesting.infrastructure.config.settings import HarvestingSettings


def get_cosmos_client(
    settings: HarvestingSettings, credential: DefaultAzureCredential
) -> CosmosClient:
    # A configured key (local/emulator auth) takes precedence over managed identity.
    effective_credential: DefaultAzureCredential | str = settings.cosmos_key or credential
    return CosmosClient(
        url=settings.cosmos_uri,
        credential=effective_credential,
        retry_throttle_total=settings.cosmos_retry_throttle_total,
        retry_throttle_backoff_max=settings.cosmos_retry_throttle_backoff_max,
    )
