import asyncio
from datetime import UTC, datetime, timedelta

from azure.keyvault.secrets.aio import SecretClient


class KeyVaultResolver:
    def __init__(self, vault_url: str, ttl_seconds: int, credential) -> None:
        if not vault_url:
            raise ValueError("KEYVAULT_URI must not be empty.")
        self._client = SecretClient(vault_url=vault_url, credential=credential)
        self._ttl = timedelta(seconds=ttl_seconds)
        self._cache: dict[str, tuple[str, datetime]] = {}
        self._lock = asyncio.Lock()

    async def resolve(self, secret_name: str) -> str:
        async with self._lock:
            entry = self._cache.get(secret_name)
            if entry and datetime.now(tz=UTC) < entry[1]:
                return entry[0]
            secret = await self._client.get_secret(secret_name)
            value = secret.value or ""
            expires_at = datetime.now(tz=UTC) + self._ttl
            self._cache[secret_name] = (value, expires_at)
            return value

    async def close(self) -> None:
        await self._client.close()
