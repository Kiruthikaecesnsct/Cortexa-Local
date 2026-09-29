import logging

from azure.identity.aio import DefaultAzureCredential
from azure.keyvault.secrets.aio import SecretClient

from evidence.domain.errors.evidence_errors import SecretResolutionError
from evidence.infrastructure.config.settings import EvidenceSettings

logger = logging.getLogger(__name__)

_LOCAL_SECRET_MAP = {
    "USPTO_API_KEY": "uspto_api_key",
    "EPO_CONSUMER_KEY": "epo_consumer_key",
    "EPO_OAUTH_SECRET": "epo_oauth_secret",
    "LENS_API_KEY": "lens_api_key",
}


class SecretsProvider:
    def __init__(
        self, settings: EvidenceSettings, credential: DefaultAzureCredential | None = None
    ) -> None:
        self._settings = settings
        self._credential = credential
        self._cache: dict[str, str] = {}
        self._config_version: int | None = None

    def invalidate_on_version_change(self, config_version: int) -> None:
        if self._config_version is not None and config_version != self._config_version:
            logger.info(
                "patent secrets cache invalidated old_version=%s new_version=%s",
                self._config_version,
                config_version,
            )
            self._cache.clear()
        self._config_version = config_version

    async def get_secret(self, name: str) -> str:
        if name in self._cache:
            return self._cache[name]

        value = await self._resolve(name)
        self._cache[name] = value
        logger.info("secret resolved name=%s", name)
        return value

    async def _resolve(self, name: str) -> str:
        if self._settings.local_dev:
            return self._resolve_local(name)
        return await self._resolve_keyvault(name)

    def _resolve_local(self, name: str) -> str:
        attr = _LOCAL_SECRET_MAP.get(name)
        if not attr:
            raise SecretResolutionError(name, "unknown secret name for local dev")
        value = getattr(self._settings, attr, "")
        if not value:
            raise SecretResolutionError(name, "env var is empty; set it in .env")
        return value

    async def _resolve_keyvault(self, name: str) -> str:
        if self._credential is None:
            raise SecretResolutionError(name, "no credential provided for Key Vault access")
        kv_name = name.lower().replace("_", "-")
        try:
            async with SecretClient(
                vault_url=self._settings.keyvault_uri,
                credential=self._credential,
            ) as client:
                secret = await client.get_secret(kv_name)
                if not secret.value:
                    raise SecretResolutionError(name, "Key Vault returned empty value")
                return secret.value
        except SecretResolutionError:
            raise
        except Exception as exc:
            raise SecretResolutionError(name, str(exc)) from exc
