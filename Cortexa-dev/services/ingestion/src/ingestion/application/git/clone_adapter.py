import asyncio
from pathlib import Path

from ingestion.application.git import size_checker, url_parser
from ingestion.domain.errors.clone_errors import AuthResolutionError
from ingestion.infrastructure.config.settings import IngestionSettings
from ingestion.infrastructure.git import git_runner
from ingestion.infrastructure.secrets.keyvault_client import KeyVaultClient


class CloneAdapter:
    def __init__(self, settings: IngestionSettings, secret_client: KeyVaultClient) -> None:
        self._settings = settings
        self._secret_client = secret_client
        self._semaphore = asyncio.Semaphore(settings.clone_max_concurrency)

    async def clone(self, raw_url: str, secret_name: str | None, branch: str | None) -> Path:
        ref = url_parser.parse(raw_url)
        token = await self._resolve_token(secret_name) if secret_name else None
        await size_checker.check_size(ref, token, self._settings)
        async with self._semaphore:
            dest = await git_runner.run_clone(ref, token, self._settings, branch)
        return Path(dest)

    async def _resolve_token(self, secret_name: str) -> str:
        token = await self._secret_client.get_secret(secret_name)
        if not token:
            raise AuthResolutionError(f"No token found for secret {secret_name!r}")
        return token
