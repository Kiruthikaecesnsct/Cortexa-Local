from dataclasses import dataclass
from typing import Protocol

from ingestion.domain.models.local_system_scan import DirectoryListing


@dataclass(frozen=True)
class SshConnectionParams:
    host: str
    port: int
    username: str
    private_key: str
    passphrase: str | None = None


class LocalSystemDirectoryReader(Protocol):
    async def list_directory(
        self, connection: SshConnectionParams, path: str
    ) -> DirectoryListing: ...
