import stat
from datetime import UTC, datetime

import asyncssh

from ingestion.application.local_system_scan.local_system_port import SshConnectionParams
from ingestion.domain.errors.local_system_scan_errors import (
    InvalidScanTargetError,
    LocalSystemAccessDeniedError,
    LocalSystemAuthError,
    LocalSystemConnectionError,
    LocalSystemPathNotFoundError,
    LocalSystemTimeoutError,
    LocalSystemUpstreamError,
)
from ingestion.domain.models.local_system_scan import DirectoryEntry, DirectoryListing
from ingestion.infrastructure.config.settings import IngestionSettings

_SKIP_NAMES = {".", ".."}


def _entry_type(permissions: int) -> str:
    if stat.S_ISDIR(permissions):
        return "directory"
    if stat.S_ISLNK(permissions):
        return "symlink"
    return "file"


def _join_path(base: str, name: str) -> str:
    return f"{base.rstrip('/')}/{name}" if base != "/" else f"/{name}"


def _to_entry(base_path: str, item: asyncssh.SFTPName) -> DirectoryEntry | None:
    if item.filename in _SKIP_NAMES:
        return None
    permissions = item.attrs.permissions or 0
    mtime = item.attrs.mtime
    return DirectoryEntry(
        name=item.filename,
        path=_join_path(base_path, item.filename),
        type=_entry_type(permissions),
        size=item.attrs.size,
        modified_at=datetime.fromtimestamp(mtime, tz=UTC) if mtime else None,
    )


class SshDirectoryReader:
    """Lists a single directory level over SFTP for the "Local File System"
    scan source. Host keys are intentionally not verified (known_hosts=None):
    the target is an ad hoc VM the caller supplies by IP on every request,
    the same trust model as pasting a PAT for GitHub/Azure DevOps scans —
    there is no durable known_hosts file to check it against, and the risk
    is scoped to whatever network path the caller already has to that VM.
    """

    def __init__(self, settings: IngestionSettings) -> None:
        self._settings = settings

    async def list_directory(
        self, connection: SshConnectionParams, path: str
    ) -> DirectoryListing:
        try:
            client_key = asyncssh.import_private_key(
                connection.private_key, passphrase=connection.passphrase
            )
        except asyncssh.KeyImportError as exc:
            raise LocalSystemAuthError(
                "The SSH key could not be read. Check the key and passphrase."
            ) from exc

        try:
            async with asyncssh.connect(
                connection.host,
                port=connection.port,
                username=connection.username,
                client_keys=[client_key],
                known_hosts=None,
                connect_timeout=self._settings.local_system_scan_timeout_seconds,
                login_timeout=self._settings.local_system_scan_timeout_seconds,
            ) as conn:
                async with conn.start_sftp_client() as sftp:
                    return await self._list(sftp, path)
        except asyncssh.PermissionDenied as exc:
            raise LocalSystemAuthError(
                "The VM rejected the SSH key for this username."
            ) from exc
        except TimeoutError as exc:
            raise LocalSystemTimeoutError("Timed out connecting to the VM.") from exc
        except (OSError, asyncssh.ConnectionLost, asyncssh.DisconnectError) as exc:
            raise LocalSystemConnectionError(
                f"Could not reach {connection.host}:{connection.port}."
            ) from exc
        except asyncssh.Error as exc:
            raise LocalSystemUpstreamError(f"SSH connection failed: {exc}") from exc

    async def _list(self, sftp: asyncssh.SFTPClient, path: str) -> DirectoryListing:
        try:
            resolved = await sftp.realpath(path)
        except asyncssh.SFTPNoSuchFile as exc:
            raise LocalSystemPathNotFoundError(f"Path not found: {path}") from exc
        except asyncssh.SFTPPermissionDenied as exc:
            raise LocalSystemAccessDeniedError(f"Permission denied: {path}") from exc

        try:
            raw_entries = await sftp.readdir(resolved)
        except asyncssh.SFTPNoSuchFile as exc:
            raise LocalSystemPathNotFoundError(f"Path not found: {resolved}") from exc
        except asyncssh.SFTPPermissionDenied as exc:
            raise LocalSystemAccessDeniedError(f"Permission denied: {resolved}") from exc
        except asyncssh.SFTPFailure as exc:
            # e.g. the path exists but is a regular file, not a directory.
            raise InvalidScanTargetError(f"{resolved} is not a directory.") from exc

        max_entries = self._settings.local_system_scan_max_entries
        mapped = [e for i in raw_entries if (e := _to_entry(resolved, i)) is not None]
        mapped.sort(key=lambda e: e.name.lower())
        truncated = len(mapped) > max_entries
        return DirectoryListing(
            path=resolved, entries=mapped[:max_entries], truncated=truncated
        )
