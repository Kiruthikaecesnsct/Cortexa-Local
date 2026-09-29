import hashlib
import logging
import os

from ingestion.infrastructure.config.settings import IngestionSettings

logger = logging.getLogger(__name__)


def _redact(value: str) -> str:
    return "sha256:" + hashlib.sha256(value.encode()).hexdigest()[:8]


class KeyVaultClient:
    def __init__(self, settings: IngestionSettings) -> None:
        self._settings = settings
        self._cache: dict[str, str] = {}
        self._client = None
        self._credential = None

    async def get_secret(self, name: str) -> str:
        if name in self._cache:
            return self._cache[name]

        value = await self._resolve(name)
        self._cache[name] = value
        logger.info("Resolved secret %s as %s", name, _redact(value))
        return value

    async def _resolve(self, name: str) -> str:
        if self._settings.keyvault_uri:
            return await self._resolve_from_keyvault(name)
        if not self._settings.local_dev:
            raise RuntimeError(
                "KEYVAULT_URI is not set and LOCAL_DEV is false. "
                "Set KEYVAULT_URI in production or LOCAL_DEV=true for local development."
            )
        return self._resolve_from_env(name)

    async def _resolve_from_keyvault(self, name: str) -> str:
        from azure.identity.aio import DefaultAzureCredential
        from azure.keyvault.secrets.aio import SecretClient

        if self._client is None:
            self._credential = DefaultAzureCredential()
            self._client = SecretClient(
                vault_url=self._settings.keyvault_uri,
                credential=self._credential,
            )

        secret = await self._client.get_secret(name)
        return secret.value or ""

    def _resolve_from_env(self, name: str) -> str:
        env_key = name.upper().replace("-", "_")
        value = os.environ.get(env_key, "")
        return value

    async def close(self) -> None:
        if self._client is not None:
            await self._client.close()
        if self._credential is not None:
            await self._credential.close()
        self._client = None
        self._credential = None
