import logging

from ingestion.application.dtos.local_system_scan_dtos import (
    ListDirectoryRequest,
    ListDirectoryResponse,
)
from ingestion.application.local_system_scan.local_system_port import (
    LocalSystemDirectoryReader,
    SshConnectionParams,
)
from ingestion.application.local_system_scan.scan_target import (
    validate_host,
    validate_path,
    validate_port,
    validate_username,
)

_logger = logging.getLogger(__name__)


class LocalSystemScanHandler:
    def __init__(self, reader: LocalSystemDirectoryReader) -> None:
        self._reader = reader

    async def list_directory(self, request: ListDirectoryRequest) -> ListDirectoryResponse:
        connection = SshConnectionParams(
            host=validate_host(request.host),
            port=validate_port(request.port),
            username=validate_username(request.username),
            private_key=request.private_key.get_secret_value(),
            passphrase=request.passphrase.get_secret_value() if request.passphrase else None,
        )
        path = validate_path(request.path)
        listing = await self._reader.list_directory(connection, path)
        listing.entries.sort(key=lambda e: (e.type != "directory", e.name.lower()))
        _logger.info(
            "Local system scan listed %d entries from %s@%s:%s",
            len(listing.entries),
            connection.username,
            connection.host,
            listing.path,
        )
        return ListDirectoryResponse(
            path=listing.path, entries=listing.entries, truncated=listing.truncated
        )
