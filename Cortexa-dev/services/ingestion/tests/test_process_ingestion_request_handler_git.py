from pathlib import Path
from unittest.mock import AsyncMock, MagicMock, patch

import httpx

from ingestion.application.handlers.process_ingestion_request_handler import (
    ProcessIngestionDeps,
    ProcessIngestionRequestHandler,
    ProcessOutcome,
)
from ingestion.application.raw_file_ingestor import IngestorConfig
from ingestion.domain.errors.clone_errors import (
    AuthResolutionError,
    CloneTimeoutError,
    GitExecutionError,
    InvalidRepoUrlError,
    RepoTooLargeError,
    UnsupportedHostError,
)
from ingestion.domain.events.event_envelope import EventEnvelope
from ingestion.domain.models.document_ref import DocumentRef

BATCH_ID = "batch-001"
DOCUMENT_ID = "doc-001"
CORRELATION_ID = "corr-001"
FILENAME = "repo"
GITHUB_URL = "https://github.com/acme/my-repo.git"

_CONFIG = IngestorConfig(chunk_size=512, chunk_overlap=50, chunk_encoding="cl100k_base")


def _make_envelope(
    *, source_kind: str = "code", repo_url: str = GITHUB_URL, **extras
) -> EventEnvelope:
    payload = {"source_kind": source_kind, "repo_url": repo_url}
    payload.update(extras)
    return EventEnvelope(
        event_type="ingestion.requested",
        batch_id=BATCH_ID,
        document_id=DOCUMENT_ID,
        correlation_id=CORRELATION_ID,
        payload=payload,
    )


def _make_document(*, status: str = "queued") -> DocumentRef:
    return DocumentRef(
        id=DOCUMENT_ID,
        batch_id=BATCH_ID,
        filename=FILENAME,
        status=status,
    )


def _make_handler(
    *,
    document: DocumentRef | None = None,
    clone_adapter: MagicMock | None = None,
) -> ProcessIngestionRequestHandler:
    document_repo = MagicMock(get=AsyncMock(return_value=document))
    store_handler = MagicMock(handle=AsyncMock())
    blob_repo = MagicMock(read=AsyncMock(return_value=(b"bytes", "text/plain")))
    return ProcessIngestionRequestHandler(
        deps=ProcessIngestionDeps(
            blob_repo=blob_repo,
            document_repo=document_repo,
            store_handler=store_handler,
            ingestor_config=_CONFIG,
            clone_adapter=clone_adapter,
        )
    )


async def test_code_pipeline_invalid_repo_url_returns_permanent():
    clone_adapter = MagicMock()
    clone_adapter.clone = AsyncMock(side_effect=InvalidRepoUrlError("bad url"))
    handler = _make_handler(document=_make_document(), clone_adapter=clone_adapter)

    outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.PERMANENT


async def test_code_pipeline_unsupported_host_returns_permanent():
    clone_adapter = MagicMock()
    clone_adapter.clone = AsyncMock(side_effect=UnsupportedHostError("unsupported host"))
    handler = _make_handler(document=_make_document(), clone_adapter=clone_adapter)

    outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.PERMANENT


async def test_code_pipeline_repo_too_large_returns_permanent():
    clone_adapter = MagicMock()
    clone_adapter.clone = AsyncMock(side_effect=RepoTooLargeError(600_000_000, 500_000_000))
    handler = _make_handler(document=_make_document(), clone_adapter=clone_adapter)

    outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.PERMANENT


async def test_code_pipeline_auth_resolution_error_returns_permanent():
    clone_adapter = MagicMock()
    clone_adapter.clone = AsyncMock(side_effect=AuthResolutionError("no token"))
    handler = _make_handler(document=_make_document(), clone_adapter=clone_adapter)

    outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.PERMANENT


async def test_code_pipeline_git_execution_error_auth_returns_permanent():
    clone_adapter = MagicMock()
    clone_adapter.clone = AsyncMock(side_effect=GitExecutionError(128, "fatal: auth failed"))
    handler = _make_handler(document=_make_document(), clone_adapter=clone_adapter)

    outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.PERMANENT


async def test_code_pipeline_git_execution_error_not_found_returns_permanent():
    clone_adapter = MagicMock()
    clone_adapter.clone = AsyncMock(
        side_effect=GitExecutionError(128, "fatal: repository not found")
    )
    handler = _make_handler(document=_make_document(), clone_adapter=clone_adapter)

    outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.PERMANENT


async def test_code_pipeline_git_execution_error_other_returns_transient():
    clone_adapter = MagicMock()
    clone_adapter.clone = AsyncMock(side_effect=GitExecutionError(1, "fatal: network issue"))
    handler = _make_handler(document=_make_document(), clone_adapter=clone_adapter)

    outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.TRANSIENT


async def test_code_pipeline_clone_timeout_returns_transient():
    clone_adapter = MagicMock()
    clone_adapter.clone = AsyncMock(side_effect=CloneTimeoutError(120.0))
    handler = _make_handler(document=_make_document(), clone_adapter=clone_adapter)

    outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.TRANSIENT


async def test_code_pipeline_httpx_timeout_returns_transient():
    clone_adapter = MagicMock()
    clone_adapter.clone = AsyncMock(side_effect=httpx.TimeoutException("timeout"))
    handler = _make_handler(document=_make_document(), clone_adapter=clone_adapter)

    outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.TRANSIENT


async def test_code_pipeline_clone_directory_removed_on_success(tmp_path):
    clone_dir = tmp_path / "clone123"
    clone_dir.mkdir()
    (clone_dir / "main.py").write_text("def main(): pass")

    clone_adapter = MagicMock()
    clone_adapter.clone = AsyncMock(return_value=Path(clone_dir))
    handler = _make_handler(document=_make_document(), clone_adapter=clone_adapter)

    with patch(
        "ingestion.application.handlers.process_ingestion_request_handler.walk_code_files",
        return_value=[("main.py", "def main(): pass")],
    ):
        with patch(
            "ingestion.application.handlers.process_ingestion_request_handler.assemble_code_ingest_request",
            return_value=MagicMock(),
        ):
            await handler.handle(_make_envelope())

    assert not clone_dir.exists()


async def test_code_pipeline_clone_directory_removed_on_walk_failure(tmp_path):
    clone_dir = tmp_path / "clone456"
    clone_dir.mkdir()

    clone_adapter = MagicMock()
    clone_adapter.clone = AsyncMock(return_value=Path(clone_dir))
    handler = _make_handler(document=_make_document(), clone_adapter=clone_adapter)

    with patch(
        "ingestion.application.handlers.process_ingestion_request_handler.walk_code_files",
        side_effect=RuntimeError("walk failed"),
    ):
        await handler.handle(_make_envelope())

    assert not clone_dir.exists()


async def test_code_pipeline_clone_directory_removed_on_chunk_failure(tmp_path):
    clone_dir = tmp_path / "clone789"
    clone_dir.mkdir()

    clone_adapter = MagicMock()
    clone_adapter.clone = AsyncMock(return_value=Path(clone_dir))
    handler = _make_handler(document=_make_document(), clone_adapter=clone_adapter)

    with patch(
        "ingestion.application.handlers.process_ingestion_request_handler.walk_code_files",
        return_value=[("file.py", "code")],
    ):
        with patch(
            "ingestion.application.handlers.process_ingestion_request_handler.assemble_code_ingest_request",
            side_effect=RuntimeError("chunk failed"),
        ):
            await handler.handle(_make_envelope())

    assert not clone_dir.exists()


async def test_code_pipeline_clone_directory_removed_on_store_failure(tmp_path):
    clone_dir = tmp_path / "clone999"
    clone_dir.mkdir()

    clone_adapter = MagicMock()
    clone_adapter.clone = AsyncMock(return_value=Path(clone_dir))
    store_handler = MagicMock(handle=AsyncMock(side_effect=RuntimeError("store failed")))
    document_repo = MagicMock(get=AsyncMock(return_value=_make_document()))
    blob_repo = MagicMock(read=AsyncMock())
    handler = ProcessIngestionRequestHandler(
        deps=ProcessIngestionDeps(
            blob_repo=blob_repo,
            document_repo=document_repo,
            store_handler=store_handler,
            ingestor_config=_CONFIG,
            clone_adapter=clone_adapter,
        )
    )

    with patch(
        "ingestion.application.handlers.process_ingestion_request_handler.walk_code_files",
        return_value=[("file.py", "code")],
    ):
        with patch(
            "ingestion.application.handlers.process_ingestion_request_handler.assemble_code_ingest_request",
            return_value=MagicMock(),
        ):
            await handler.handle(_make_envelope())

    assert not clone_dir.exists()


async def test_code_pipeline_paper_source_kind_routes_to_blob_path():
    handler = _make_handler(document=_make_document(), clone_adapter=MagicMock())
    envelope = _make_envelope(source_kind="paper")

    with patch(
        "ingestion.application.handlers.process_ingestion_request_handler.assemble_ingest_request",
        new_callable=AsyncMock,
        return_value=MagicMock(),
    ) as mock_assemble:
        outcome = await handler.handle(envelope)

    assert outcome == ProcessOutcome.SUCCESS
    mock_assemble.assert_awaited_once()


async def test_code_pipeline_missing_source_kind_routes_to_blob_path():
    handler = _make_handler(document=_make_document(), clone_adapter=MagicMock())
    envelope = EventEnvelope(
        event_type="ingestion.requested",
        batch_id=BATCH_ID,
        document_id=DOCUMENT_ID,
        correlation_id=CORRELATION_ID,
        payload={},
    )

    with patch(
        "ingestion.application.handlers.process_ingestion_request_handler.assemble_ingest_request",
        new_callable=AsyncMock,
        return_value=MagicMock(),
    ) as mock_assemble:
        outcome = await handler.handle(envelope)

    assert outcome == ProcessOutcome.SUCCESS
    mock_assemble.assert_awaited_once()


async def test_code_pipeline_success_returns_success(tmp_path):
    clone_dir = tmp_path / "clone_success"
    clone_dir.mkdir()

    clone_adapter = MagicMock()
    clone_adapter.clone = AsyncMock(return_value=Path(clone_dir))
    handler = _make_handler(document=_make_document(), clone_adapter=clone_adapter)

    with patch(
        "ingestion.application.handlers.process_ingestion_request_handler.walk_code_files",
        return_value=[("file.py", "code")],
    ):
        with patch(
            "ingestion.application.handlers.process_ingestion_request_handler.assemble_code_ingest_request",
            return_value=MagicMock(),
        ):
            outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.SUCCESS


async def test_code_pipeline_no_clone_adapter_returns_transient():
    handler = _make_handler(document=_make_document(), clone_adapter=None)

    outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.TRANSIENT
