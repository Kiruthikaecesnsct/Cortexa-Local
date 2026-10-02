from pathlib import Path
from unittest.mock import AsyncMock, MagicMock

import pytest

from ingestion.application.handlers.process_ingestion_request_handler import (
    ProcessIngestionDeps,
    ProcessIngestionRequestHandler,
    ProcessOutcome,
)
from ingestion.application.raw_file_ingestor import IngestorConfig
from ingestion.application.repository_clone.azure_devops_source import AzureDevOpsSource
from ingestion.application.repository_clone.github_source import GitHubSource
from ingestion.application.repository_clone.saved_repository_loader import SavedRepositoryLoader
from ingestion.domain.enums.source_provider import SourceProvider
from ingestion.domain.errors.scan_errors import (
    CloneNotFoundError,
    CloneStorageUnavailableError,
)
from ingestion.domain.events.event_envelope import EventEnvelope
from ingestion.domain.models.document_ref import DocumentRef, SavedRepositoryRef
from ingestion.domain.models.repository_clone import SaveTarget

BATCH_ID = "batch-001"
DOCUMENT_ID = "doc-001"
_CONFIG = IngestorConfig(chunk_size=512, chunk_overlap=50, chunk_encoding="cl100k_base")
SAVED = SavedRepositoryRef(
    provider=SourceProvider.AZURE_DEVOPS, owner="contoso", repository="Platform/api", branch="main"
)


class FakeReader:
    def __init__(self, error: Exception | None = None) -> None:
        self.error = error
        self.fetched: list[tuple[SaveTarget, Path]] = []

    async def fetch_folder(self, target: SaveTarget, destination: Path) -> None:
        self.fetched.append((target, destination))
        if self.error:
            raise self.error
        (destination / "src").mkdir()
        (destination / "src" / "app.py").write_text("def main():\n    return 1\n")


def _loader(reader: FakeReader, workdir: Path) -> SavedRepositoryLoader:
    sources = {
        SourceProvider.GITHUB: GitHubSource(MagicMock(), "https://github.com"),
        SourceProvider.AZURE_DEVOPS: AzureDevOpsSource(MagicMock(), "https://dev.azure.com"),
    }
    return SavedRepositoryLoader(reader, sources, str(workdir))


def _envelope() -> EventEnvelope:
    return EventEnvelope(
        event_type="ingestion.requested",
        batch_id=BATCH_ID,
        document_id=DOCUMENT_ID,
        correlation_id="corr-001",
        payload={},
    )


def _handler(saved: SavedRepositoryRef, loader: SavedRepositoryLoader) -> tuple:
    document = DocumentRef(
        id=DOCUMENT_ID, batch_id=BATCH_ID, filename="contoso-api@main", saved_repository=saved
    )
    store_handler = MagicMock(handle=AsyncMock())
    blob_repo = MagicMock(read=AsyncMock())
    handler = ProcessIngestionRequestHandler(
        deps=ProcessIngestionDeps(
            blob_repo=blob_repo,
            document_repo=MagicMock(get=AsyncMock(return_value=document)),
            store_handler=store_handler,
            ingestor_config=_CONFIG,
            saved_repositories=loader,
        )
    )
    return handler, store_handler, blob_repo


async def test_saved_repository_is_ingested_as_code_and_temp_folder_removed(
    tmp_path: Path,
) -> None:
    reader = FakeReader()
    handler, store_handler, blob_repo = _handler(SAVED, _loader(reader, tmp_path))

    outcome = await handler.handle(_envelope())

    assert outcome == ProcessOutcome.SUCCESS
    [(target, folder)] = reader.fetched
    assert target == SaveTarget(SourceProvider.AZURE_DEVOPS, "contoso", "Platform/api", "main")
    assert folder.parent == tmp_path
    assert not folder.exists()
    request = store_handler.handle.await_args.args[0]
    assert request.document_id == DOCUMENT_ID
    assert "def main" in str(request)
    blob_repo.read.assert_not_awaited()


@pytest.mark.parametrize(
    ("error", "expected"),
    [
        (CloneNotFoundError("Saved repository not found."), ProcessOutcome.PERMANENT),
        (CloneStorageUnavailableError("down"), ProcessOutcome.TRANSIENT),
    ],
)
async def test_storage_errors_are_classified(
    tmp_path: Path, error: Exception, expected: ProcessOutcome
) -> None:
    handler, store_handler, _ = _handler(SAVED, _loader(FakeReader(error), tmp_path))

    assert await handler.handle(_envelope()) == expected
    store_handler.handle.assert_not_awaited()
    assert list(tmp_path.iterdir()) == []


async def test_invalid_saved_names_fail_permanently_without_fetching(tmp_path: Path) -> None:
    reader = FakeReader()
    bad = SavedRepositoryRef(
        provider=SourceProvider.GITHUB, owner="acme", repository="api", branch="../../etc"
    )
    handler, _, _ = _handler(bad, _loader(reader, tmp_path))

    assert await handler.handle(_envelope()) == ProcessOutcome.PERMANENT
    assert reader.fetched == []


def test_document_ref_reads_saved_repository_from_the_row() -> None:
    row = {
        "id": DOCUMENT_ID,
        "batch_id": BATCH_ID,
        "filename": "x",
        "saved_repository": {
            "provider": "github",
            "owner": "acme",
            "repository": "api",
            "branch": "main",
        },
    }

    document = DocumentRef.model_validate(row)

    assert document.saved_repository == SavedRepositoryRef(
        provider=SourceProvider.GITHUB, owner="acme", repository="api", branch="main"
    )
