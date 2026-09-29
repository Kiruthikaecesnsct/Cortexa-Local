from azure.cosmos.aio import CosmosClient
from azure.identity.aio import DefaultAzureCredential

from scoring.infrastructure.config.settings import ScoringSettings


def get_cosmos_client(
    settings: ScoringSettings, credential: DefaultAzureCredential
) -> CosmosClient:
    return CosmosClient(url=settings.cosmos_uri, credential=credential)
