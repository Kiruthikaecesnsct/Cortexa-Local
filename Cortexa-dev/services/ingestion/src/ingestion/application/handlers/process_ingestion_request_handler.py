import logging
from dataclasses import dataclass
from enum import Enum

import httpx
from pydantic import ValidationError

from ingestion.application.code_ingestor import assemble_code_ingest_request
from ingestion.application.code_walker import walk_code_files
from ingestion.application.git.clone_adapter import CloneAdapter
from ingestion.application.handlers.store_ingestion_handler import StoreIngestionHandler
from ingestion.application.raw_file_ingestor import IngestorConfig, assemble_ingest_request
from ingestion.domain.errors.clone_errors import (
    AuthResolutionError,
    CloneTimeoutError,
    GitExecutionError,
    InvalidRepoUrlError,
    RepoTooLargeError,
    UnsupportedHostError,
)
from ingestion.domain.errors.parser_errors import UnsupportedFormatError
from ingestion.domain.errors.storage_errors import StorageWriteError
from ingestion.domain.events.event_envelope import EventEnvelope
from ingestion.domain.models.document_ref import DocumentRef
from ingestion.domain.repositories.storage_protocols import BlobRepository, DocumentRepository
from ingestion.infrastructure.git.clone_cleanup import remove_clone

logger = logging.getLogger(__name__)


class ProcessOutcome(Enum):
    SUCCESS = "success"
    PERMANENT = "permanent"
    TRANSIENT = "transient"


@dataclass
class ProcessIngestionDeps:
    blob_repo: BlobRepository
    document_repo: DocumentRepository
    store_handler: StoreIngestionHandler
    ingestor_config: IngestorConfig
    clone_adapter: CloneAdapter | None = None


class ProcessIngestionRequestHandler:
    def __init__(self, deps: ProcessIngestionDeps) -> None:
        self._blob = deps.blob_repo
        self._documents = deps.document_repo
        self._store = deps.store_handler
        self._config = deps.ingestor_config
        self._clone_adapter = deps.clone_adapter

    async def handle(self, envelope: EventEnvelope) -> ProcessOutcome:
        if envelope.document_id is None:
            logger.error(
                "batch_id=%s correlation_id=%s outcome=PERMANENT reason=missing_document_id",
                envelope.batch_id,
                envelope.correlation_id,
            )
            return ProcessOutcome.PERMANENT
        try:
            return await self._execute(envelope)
        except (
            InvalidRepoUrlError,
            UnsupportedHostError,
            AuthResolutionError,
            RepoTooLargeError,
        ) as exc:
            logger.error(
                "batch_id=%s document_id=%s correlation_id=%s "
                "outcome=PERMANENT reason=repo_config_error error=%s",
                envelope.batch_id,
                envelope.document_id,
                envelope.correlation_id,
                exc,
            )
            return ProcessOutcome.PERMANENT
        except GitExecutionError as exc:
            if exc.exit_code == 128 and (
                "auth" in exc.args[0].lower() or "not found" in exc.args[0].lower()
            ):
                logger.error(
                    "batch_id=%s document_id=%s correlation_id=%s "
                    "outcome=PERMANENT reason=git_auth_or_notfound error=%s",
                    envelope.batch_id,
                    envelope.document_id,
                    envelope.correlation_id,
                    exc,
                )
                return ProcessOutcome.PERMANENT
            logger.error(
                "batch_id=%s document_id=%s correlation_id=%s "
                "outcome=TRANSIENT reason=git_execution_error error=%s",
                envelope.batch_id,
                envelope.document_id,
                envelope.correlation_id,
                exc,
            )
            return ProcessOutcome.TRANSIENT
        except (CloneTimeoutError, httpx.TimeoutException) as exc:
            logger.error(
                "batch_id=%s document_id=%s correlation_id=%s "
                "outcome=TRANSIENT reason=timeout error=%s",
                envelope.batch_id,
                envelope.document_id,
                envelope.correlation_id,
                exc,
            )
            return ProcessOutcome.TRANSIENT
        except UnsupportedFormatError as exc:
            logger.error(
                "batch_id=%s document_id=%s correlation_id=%s "
                "outcome=PERMANENT reason=unsupported_format error=%s",
                envelope.batch_id,
                envelope.document_id,
                envelope.correlation_id,
                exc,
            )
            return ProcessOutcome.PERMANENT
        except ValidationError as exc:
            logger.error(
                "batch_id=%s document_id=%s correlation_id=%s "
                "outcome=PERMANENT reason=malformed_document error=%s",
                envelope.batch_id,
                envelope.document_id,
                envelope.correlation_id,
                exc,
            )
            return ProcessOutcome.PERMANENT
        except StorageWriteError as exc:
            return self._classify_storage_error(envelope, exc)
        except ValueError as exc:
            # A ValueError reaching here is deterministic (tokenization/chunking or
            # bad config) — every I/O, clone, and timeout failure raises its own
            # typed error above. Retrying it would loop forever, so fail once.
            logger.error(
                "batch_id=%s document_id=%s correlation_id=%s "
                "outcome=PERMANENT reason=deterministic_processing_error error=%s",
                envelope.batch_id,
                envelope.document_id,
                envelope.correlation_id,
                exc,
            )
            return ProcessOutcome.PERMANENT
        except Exception as exc:
            logger.exception(
                "batch_id=%s document_id=%s correlation_id=%s outcome=TRANSIENT error=%s",
                envelope.batch_id,
                envelope.document_id,
                envelope.correlation_id,
                exc,
            )
            return ProcessOutcome.TRANSIENT

    def _classify_storage_error(
        self, envelope: EventEnvelope, exc: StorageWriteError
    ) -> ProcessOutcome:
        # A StorageWriteError wrapping a ValueError is a deterministic client-side
        # rejection (e.g. Cosmos "Id contains illegal chars") — retrying can never
        # succeed, so fail once. A genuine backend/network outage is TRANSIENT.
        deterministic = isinstance(exc.__cause__, ValueError)
        outcome = ProcessOutcome.PERMANENT if deterministic else ProcessOutcome.TRANSIENT
        reason = "deterministic_storage_error" if deterministic else "storage_write_error"
        logger.error(
            "batch_id=%s document_id=%s correlation_id=%s outcome=%s reason=%s error=%s",
            envelope.batch_id,
            envelope.document_id,
            envelope.correlation_id,
            outcome.value.upper(),
            reason,
            exc,
        )
        return outcome

    async def _execute(self, envelope: EventEnvelope) -> ProcessOutcome:
        batch_id = envelope.batch_id
        document_id = envelope.document_id
        correlation_id = envelope.correlation_id

        document = await self._documents.get(document_id, batch_id)
        if document is None:
            logger.error(
                "batch_id=%s document_id=%s correlation_id=%s "
                "outcome=PERMANENT reason=document_not_found",
                batch_id,
                document_id,
                correlation_id,
            )
            return ProcessOutcome.PERMANENT

        if document.status == "completed":
            logger.info(
                "batch_id=%s document_id=%s correlation_id=%s outcome=SUCCESS reason=idempotent",
                batch_id,
                document_id,
                correlation_id,
            )
            return ProcessOutcome.SUCCESS

        return await self._run_pipeline(envelope, document)

    async def _run_pipeline(self, envelope: EventEnvelope, document: DocumentRef) -> ProcessOutcome:
        batch_id = envelope.batch_id
        document_id = envelope.document_id
        correlation_id = envelope.correlation_id

        source_kind = envelope.payload.get("source_kind", "paper")

        if source_kind == "code":
            request = await self._run_code_pipeline(envelope, document)
        else:
            request = await self._run_paper_pipeline(envelope, document)

        await self._store.handle(request)
        logger.info(
            "batch_id=%s document_id=%s correlation_id=%s outcome=SUCCESS source_kind=%s",
            batch_id,
            document_id,
            correlation_id,
            source_kind,
        )
        return ProcessOutcome.SUCCESS

    async def _run_paper_pipeline(self, envelope: EventEnvelope, document: DocumentRef):
        batch_id = envelope.batch_id
        document_id = envelope.document_id
        correlation_id = envelope.correlation_id

        raw_content, content_type = await self._blob.read(batch_id, document_id)
        return await assemble_ingest_request(
            batch_id=batch_id,
            document_id=document_id,
            filename=document.filename,
            content_type=content_type,
            raw_content=raw_content,
            correlation_id=correlation_id,
            config=self._config,
        )

    async def _run_code_pipeline(self, envelope: EventEnvelope, document: DocumentRef):
        if self._clone_adapter is None:
            raise RuntimeError("CloneAdapter not configured for code ingestion")

        batch_id = envelope.batch_id
        document_id = envelope.document_id
        correlation_id = envelope.correlation_id
        payload = envelope.payload

        repo_url = payload.get("repo_url")
        git_branch = payload.get("git_branch")
        git_pat_secret_name = payload.get("git_pat_secret_name")

        if not repo_url:
            raise InvalidRepoUrlError("Missing repo_url in payload for source_kind=code")

        clone_path = None
        try:
            clone_path = await self._clone_adapter.clone(repo_url, git_pat_secret_name, git_branch)
            files = list(walk_code_files(clone_path))

            return assemble_code_ingest_request(
                batch_id=batch_id,
                document_id=document_id,
                files=files,
                correlation_id=correlation_id,
                config=self._config,
            )
        finally:
            if clone_path:
                remove_clone(str(clone_path))
