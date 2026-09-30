import asyncio
import logging
from contextlib import asynccontextmanager

from azure.identity.aio import DefaultAzureCredential
from fastapi import FastAPI, Request
from fastapi.responses import JSONResponse, Response
from starlette.middleware.base import BaseHTTPMiddleware

from ingestion.api.routes.azure_devops_scan_routes import router as azure_devops_scan_router
from ingestion.api.routes.github_scan_routes import router as github_scan_router
from ingestion.api.routes.ingestion_routes import router as ingestion_router
from ingestion.application.git.clone_adapter import CloneAdapter
from ingestion.application.handlers.azure_devops_scan_handler import AzureDevOpsScanHandler
from ingestion.application.handlers.github_scan_handler import GitHubScanHandler
from ingestion.application.handlers.process_ingestion_request_handler import (
    ProcessIngestionDeps,
    ProcessIngestionRequestHandler,
)
from ingestion.application.handlers.store_ingestion_handler import (
    StoreIngestionDeps,
    StoreIngestionHandler,
)
from ingestion.application.raw_file_ingestor import IngestorConfig
from ingestion.infrastructure.azure_devops.azure_devops_api_client import (
    AzureDevOpsApiClient,
    create_azure_devops_http_client,
)
from ingestion.infrastructure.blob.blob_client import get_blob_service_client
from ingestion.infrastructure.blob.blob_repository import BlobRepository
from ingestion.infrastructure.config.settings import IngestionSettings
from ingestion.infrastructure.cosmos.batch_state_gate import TerminalBatchGate
from ingestion.infrastructure.cosmos.chunk_repository import ChunkRepository
from ingestion.infrastructure.cosmos.cosmos_client import get_cosmos_client
from ingestion.infrastructure.cosmos.document_repository import DocumentRepository
from ingestion.infrastructure.cosmos.provenance_repository import ProvenanceRepository
from ingestion.infrastructure.git.clone_cleanup import sweep_workdir
from ingestion.infrastructure.github.github_api_client import (
    GitHubApiClient,
    create_github_http_client,
)
from ingestion.infrastructure.observability.consumer_supervisor import (
    ConsumerHealth,
    configure_logging,
    supervise,
)
from ingestion.infrastructure.observability.sentry import configure_sentry
from ingestion.infrastructure.secrets.keyvault_client import KeyVaultClient
from ingestion.infrastructure.servicebus.event_publisher import EventPublisher
from ingestion.infrastructure.servicebus.servicebus_client import get_servicebus_client
from ingestion.infrastructure.servicebus.servicebus_consumer import ServiceBusConsumer

configure_logging()
_logger = logging.getLogger(__name__)


class _ContentSizeLimitMiddleware(BaseHTTPMiddleware):
    def __init__(self, app, max_bytes: int) -> None:
        super().__init__(app)
        self._max_bytes = max_bytes

    async def dispatch(self, request: Request, call_next):
        content_length = request.headers.get("content-length")
        if content_length and int(content_length) > self._max_bytes:
            return JSONResponse({"detail": "Request body too large."}, status_code=413)
        return await call_next(request)


@asynccontextmanager
async def lifespan(app: FastAPI):
    settings = IngestionSettings()
    configure_sentry(settings)
    credential = DefaultAzureCredential()

    sweep_workdir(settings.clone_workdir)

    cosmos = get_cosmos_client(settings, credential)
    db = cosmos.get_database_client(settings.cosmos_database)

    blob_svc = get_blob_service_client(settings, credential)
    sb_client = get_servicebus_client(settings, credential)

    blob_repo = BlobRepository(blob_svc, settings.blob_raw_container)
    viewable_blob_repo = BlobRepository(
        blob_svc, settings.blob_viewable_container, blob_name_suffix=".pdf"
    )
    doc_repo = DocumentRepository(db.get_container_client(settings.documents_container))
    chunk_repo = ChunkRepository(db.get_container_client(settings.chunks_container))
    batches_container = db.get_container_client(settings.batches_container)
    gate = TerminalBatchGate(batches_container, settings.batch_terminal_cache_ttl_seconds)

    kv_client = KeyVaultClient(settings)
    clone_adapter = CloneAdapter(settings, kv_client)

    store_handler = StoreIngestionHandler(
        deps=StoreIngestionDeps(
            blob_repo=blob_repo,
            document_repo=doc_repo,
            chunk_repo=chunk_repo,
            provenance_repo=ProvenanceRepository(
                db.get_container_client(settings.provenance_container)
            ),
            event_publisher=EventPublisher(sb_client),
            ingestion_completed_topic=settings.ingestion_completed_topic,
            viewable_blob_repo=viewable_blob_repo,
        )
    )
    app.state.store_ingestion_handler = store_handler

    github_http = create_github_http_client(settings)
    app.state.github_scan_handler = GitHubScanHandler(
        GitHubApiClient(github_http, settings.github_scan_max_pages)
    )

    azure_devops_http = create_azure_devops_http_client(settings)
    app.state.azure_devops_scan_handler = AzureDevOpsScanHandler(
        AzureDevOpsApiClient(azure_devops_http, settings.azdo_api_version)
    )

    ingestor_config = IngestorConfig(
        chunk_size=settings.chunk_size_tokens,
        chunk_overlap=settings.chunk_overlap_tokens,
        chunk_encoding=settings.chunk_encoding,
        docx_convert_timeout_seconds=settings.docx_convert_timeout_seconds,
        geometry_enabled=settings.geometry_enabled,
        geometry_max_words_per_page=settings.geometry_max_words_per_page,
    )
    process_handler = ProcessIngestionRequestHandler(
        deps=ProcessIngestionDeps(
            blob_repo=blob_repo,
            document_repo=doc_repo,
            store_handler=store_handler,
            ingestor_config=ingestor_config,
            clone_adapter=clone_adapter,
        )
    )
    consumer = ServiceBusConsumer(
        client=sb_client,
        handler=process_handler,
        topic=settings.ingestion_requested_topic,
        subscription=settings.ingestion_subscription,
        max_attempts=settings.consumer_max_attempts,
        session_idle_timeout=settings.consumer_session_idle_timeout,
        gate=gate,
    )

    health = ConsumerHealth()
    app.state.consumer_health = health

    cancel_event = asyncio.Event()
    consumer_task = asyncio.create_task(consumer.run(cancel_event))
    health.mark_alive()
    supervise(consumer_task, health, _logger)

    _logger.info("Ingestion service started")
    yield

    cancel_event.set()
    consumer_task.cancel()
    try:
        await consumer_task
    except asyncio.CancelledError:
        pass

    await github_http.aclose()
    await azure_devops_http.aclose()
    await kv_client.close()
    await cosmos.close()
    await blob_svc.close()
    await sb_client.close()
    await credential.close()
    _logger.info("Ingestion service stopped")


def create_app() -> FastAPI:
    cfg = IngestionSettings()
    app = FastAPI(title="ingestion", version="0.1.0", lifespan=lifespan)
    app.add_middleware(_ContentSizeLimitMiddleware, max_bytes=cfg.raw_file_max_bytes)

    @app.get("/health", response_model=None)
    async def health() -> dict[str, str] | Response:
        consumer_health = getattr(app.state, "consumer_health", None)
        if consumer_health is None or consumer_health.is_serving:
            return {"status": "ok"}
        return JSONResponse(status_code=503, content={"status": "consumer_dead"})

    app.include_router(ingestion_router)
    app.include_router(github_scan_router)
    app.include_router(azure_devops_scan_router)
    return app


app = create_app()
