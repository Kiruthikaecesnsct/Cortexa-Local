import pytest

from ingestion.application.local_system_scan.scan_target import (
    validate_host,
    validate_path,
    validate_port,
    validate_username,
)
from ingestion.domain.errors.local_system_scan_errors import InvalidScanTargetError


@pytest.mark.parametrize(
    "host",
    [
        "10.0.0.1",
        "192.168.1.20",
        "::1",
        "2001:db8::1",
        "my-vm.internal",
        "vm1",
        "a.b.c.example.com",
    ],
)
def test_validate_host_accepts_valid_hosts(host: str) -> None:
    assert validate_host(host) == host


@pytest.mark.parametrize("host", ["", "   ", "-badstart", "bad_host!", "a" * 256])
def test_validate_host_rejects_invalid_hosts(host: str) -> None:
    with pytest.raises(InvalidScanTargetError):
        validate_host(host)


@pytest.mark.parametrize("port", [1, 22, 8022, 65535])
def test_validate_port_accepts_valid_ports(port: int) -> None:
    assert validate_port(port) == port


@pytest.mark.parametrize("port", [0, -1, 65536, 100000])
def test_validate_port_rejects_invalid_ports(port: int) -> None:
    with pytest.raises(InvalidScanTargetError):
        validate_port(port)


@pytest.mark.parametrize("username", ["root", "ubuntu", "deploy-user", "svc_account.1"])
def test_validate_username_accepts_valid_names(username: str) -> None:
    assert validate_username(username) == username


@pytest.mark.parametrize("username", ["", "  ", "user name", "user/name", "a" * 65])
def test_validate_username_rejects_invalid_names(username: str) -> None:
    with pytest.raises(InvalidScanTargetError):
        validate_username(username)


@pytest.mark.parametrize("path", ["/", "/var/data", "/home/ubuntu/project", "~", "~/project"])
def test_validate_path_accepts_valid_paths(path: str) -> None:
    assert validate_path(path) == path


@pytest.mark.parametrize("path", ["", "  ", "relative/path", "var/data", "no-leading-slash"])
def test_validate_path_rejects_invalid_paths(path: str) -> None:
    with pytest.raises(InvalidScanTargetError):
        validate_path(path)
