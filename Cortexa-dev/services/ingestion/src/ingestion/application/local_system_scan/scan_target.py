import ipaddress
import re

from ingestion.domain.errors.local_system_scan_errors import InvalidScanTargetError

_MAX_HOST_LENGTH = 255
_MAX_USERNAME_LENGTH = 64
_MAX_PATH_LENGTH = 4096
# RFC 1123 hostname label: alphanumeric, single interior hyphens, 1-63 chars.
_HOSTNAME = re.compile(
    r"^(?=.{1,255}$)[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?"
    r"(?:\.[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?)*$"
)
# POSIX-ish username: no whitespace, no path separators.
_USERNAME = re.compile(r"^[A-Za-z0-9._-]{1,64}$")


def validate_host(host: str) -> str:
    value = host.strip()
    if not value or len(value) > _MAX_HOST_LENGTH:
        raise InvalidScanTargetError("Enter a valid IP address or hostname.")
    try:
        ipaddress.ip_address(value)
        return value
    except ValueError:
        pass
    if _HOSTNAME.match(value):
        return value
    raise InvalidScanTargetError("Enter a valid IP address or hostname.")


def validate_port(port: int) -> int:
    if not 1 <= port <= 65535:
        raise InvalidScanTargetError("Port must be between 1 and 65535.")
    return port


def validate_username(username: str) -> str:
    value = username.strip()
    if not _USERNAME.match(value):
        raise InvalidScanTargetError("Enter a valid SSH username.")
    return value


def validate_path(path: str) -> str:
    value = path.strip()
    if not value or len(value) > _MAX_PATH_LENGTH or "\x00" in value:
        raise InvalidScanTargetError("Enter a folder path to browse.")
    # Absolute ("/var/data") or home-relative ("~" / "~/projects") — resolved
    # server-side against the remote filesystem, never against this host's.
    if value == "~" or value.startswith(("/", "~/")):
        return value
    raise InvalidScanTargetError("Path must be absolute (e.g. /var/data) or start with ~.")
