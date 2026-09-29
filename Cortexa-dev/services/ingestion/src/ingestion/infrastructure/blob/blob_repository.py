import logging

from azure.storage.blob.aio import BlobServiceClient

logger = logging.getLogger(__name__)


class BlobRepository:
    def __init__(
        self, client: BlobServiceClient, container: str, blob_name_suffix: str = ""
    ) -> None:
        self._client = client
        self._container = container
        self._suffix = blob_name_suffix

    def _blob_name(self, batch_id: str, document_id: str) -> str:
        return f"{batch_id}/{document_id}{self._suffix}"

    def build_uri(self, batch_id: str, document_id: str) -> str:
        blob_name = self._blob_name(batch_id, document_id)
        return self._client.get_blob_client(container=self._container, blob=blob_name).url

    async def save(self, batch_id: str, document_id: str, content: bytes) -> str:
        blob_name = self._blob_name(batch_id, document_id)
        blob_client = self._client.get_blob_client(container=self._container, blob=blob_name)
        await blob_client.upload_blob(content, overwrite=True)
        return blob_client.url

    async def delete(self, blob_uri: str) -> None:
        parts = blob_uri.split(f"/{self._container}/", 1)
        if len(parts) < 2:
            logger.error("Cannot parse blob URI for rollback: %s", blob_uri)
            return
        blob_client = self._client.get_blob_client(container=self._container, blob=parts[1])
        await blob_client.delete_blob()

    async def read(self, batch_id: str, document_id: str) -> tuple[bytes, str]:
        blob_name = self._blob_name(batch_id, document_id)
        blob_client = self._client.get_blob_client(container=self._container, blob=blob_name)
        downloader = await blob_client.download_blob()
        data = await downloader.readall()
        content_type: str = downloader.properties.content_settings.content_type or ""
        return data, content_type
