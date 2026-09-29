from azure.identity.aio import DefaultAzureCredential
from azure.storage.blob.aio import BlobServiceClient

from ingestion.infrastructure.config.settings import IngestionSettings


def get_blob_service_client(
    settings: IngestionSettings, credential: DefaultAzureCredential
) -> BlobServiceClient:
    if not settings.blob_account_url:
        raise ValueError("blob_account_url is not configured.")
    return BlobServiceClient(account_url=settings.blob_account_url, credential=credential)
