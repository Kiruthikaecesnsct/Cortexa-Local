import pytest
from pydantic import SecretStr

from ingestion.application.dtos.local_system_scan_dtos import ListDirectoryRequest
from ingestion.application.handlers.local_system_scan_handler import LocalSystemScanHandler
from ingestion.application.local_system_scan.local_system_port import SshConnectionParams
from ingestion.domain.errors.local_system_scan_errors import InvalidScanTargetError
from ingestion.domain.models.local_system_scan import DirectoryEntry, DirectoryListing

HOST = "10.0.0.5"
USERNAME = "ubuntu"
PRIVATE_KEY = "-----BEGIN OPENSSH PRIVATE KEY-----\nfake\n-----END OPENSSH PRIVATE KEY-----"


class FakeReader:
    def __init__(self) -> None:
        self.calls: list[tuple[SshConnectionParams, str]] = []

    async def list_directory(self, connection: SshConnectionParams, path: str) -> DirectoryListing:
        self.calls.append((connection, path))
        return DirectoryListing(
            path=path,
            entries=[
                DirectoryEntry(name="zeta.txt", path=f"{path}/zeta.txt", type="file", size=10),
                DirectoryEntry(name="Alpha", path=f"{path}/Alpha", type="directory"),
                DirectoryEntry(name="beta.txt", path=f"{path}/beta.txt", type="file", size=5),
            ],
        )


@pytest.fixture
def reader() -> FakeReader:
    return FakeReader()


def _request(**overrides: object) -> ListDirectoryRequest:
    fields: dict[str, object] = {
        "host": HOST,
        "username": USERNAME,
        "private_key": SecretStr(PRIVATE_KEY),
        "path": "/var/data",
    }
    fields.update(overrides)
    return ListDirectoryRequest(**fields)


async def test_list_directory_sorts_directories_first_then_by_name(reader: FakeReader) -> None:
    handler = LocalSystemScanHandler(reader)

    result = await handler.list_directory(_request())

    assert [e.name for e in result.entries] == ["Alpha", "beta.txt", "zeta.txt"]
    assert reader.calls[0][1] == "/var/data"
    assert reader.calls[0][0].host == HOST
    assert reader.calls[0][0].private_key == PRIVATE_KEY


async def test_list_directory_rejects_relative_path_without_calling_reader(
    reader: FakeReader,
) -> None:
    handler = LocalSystemScanHandler(reader)

    with pytest.raises(InvalidScanTargetError):
        await handler.list_directory(_request(path="relative/path"))
    assert reader.calls == []


async def test_list_directory_rejects_invalid_host_without_calling_reader(
    reader: FakeReader,
) -> None:
    handler = LocalSystemScanHandler(reader)

    with pytest.raises(InvalidScanTargetError):
        await handler.list_directory(_request(host="bad_host!"))
    assert reader.calls == []


def test_request_repr_does_not_expose_private_key() -> None:
    request = _request()

    assert PRIVATE_KEY not in repr(request)
    assert PRIVATE_KEY not in str(request.model_dump())
