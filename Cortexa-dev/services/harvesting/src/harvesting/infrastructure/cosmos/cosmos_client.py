from azure.cosmos.aio import CosmosClient
from azure.identity.aio import DefaultAzureCredential

from harvesting.infrastructure.config.settings import HarvestingSettings


def get_cosmos_client(
    settings: HarvestingSettings, credential: DefaultAzureCredential
) -> CosmosClient:
    return CosmosClient(
        url=settings.cosmos_uri,
        credential=credential,
        retry_throttle_total=settings.cosmos_retry_throttle_total,
        retry_throttle_backoff_max=settings.cosmos_retry_throttle_backoff_max,
    )
