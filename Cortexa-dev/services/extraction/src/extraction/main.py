import asyncio
import logging
from contextlib import asynccontextmanager

import httpx
from azure.identity.aio import DefaultAzureCredential
from fastapi import FastAPI
from fastapi.responses import JSONResponse, Response

from extraction.api.routes.extraction_routes import router as extraction_router
from extraction.application.handlers.extract_candidates_handler import ExtractCandidatesHandler
from extraction.application.handlers.process_extraction_request_handler import (
    ProcessExtractionDeps,
    ProcessExtractionRequestHandler,
)
from extraction.application.handlers.store_extraction_handler import (
    StoreExtractionDeps,
    StoreExtractionHandler,
)
from extraction.application.parsers.candidate_parser import CandidateParser
from extraction.application.prompts.extraction_prompt_builder import ExtractionPromptBuilder
from extraction.infrastructure.config.settings import ExtractionSettings
from extraction.infrastructure.cosmos.batch_state_gate import TerminalBatchGate
from extraction.infrastructure.cosmos.candidate_repository import CandidateRepository
from extraction.infrastructure.cosmos.chunk_reader import ChunkReader, DocumentSourceReader
from extraction.infrastructure.cosmos.cosmos_client import get_cosmos_client
from extraction.infrastructure.model_router.model_router_client import (
    ModelRouterClient,
    RetryConfig,
)
from extraction.infrastructure.observability.consumer_supervisor import (
    ConsumerHealth,
    configure_logging,
    supervise,
)
from extraction.infrastructure.observability.sentry import configure_sentry
from extraction.infrastructure.servicebus.event_publisher import EventPublisher
from extraction.infrastructure.servicebus.servicebus_client import get_servicebus_client
from extraction.infrastructure.servicebus.servicebus_consumer import (
    ServiceBusConsumer,
    ServiceBusConsumerConfig,
)

configure_logging()
logger = logging.getLogger(__name__)


@asynccontextmanager
async def lifespan(app: FastAPI):
    settings = ExtractionSettings()
    configure_sentry(settings)
    credential = DefaultAzureCredential()

    http_client = httpx.AsyncClient()
    retry_config = RetryConfig(
        max_retries=settings.model_call_max_retries,
        backoff_max=settings.model_call_backoff_max_seconds,
        honor_retry_after=settings.model_call_honor_retry_after,
        total_budget=settings.model_call_total_retry_budget_seconds,
    )
    client = ModelRouterClient.with_retries(
        http_client=http_client,
        base_url=settings.model_router_url,
        timeout=settings.model_call_timeout_seconds,
        retry_config=retry_config,
    )
    builder = ExtractionPromptBuilder(max_output_tokens=settings.model_max_output_tokens)
    parser = CandidateParser()
    handler = ExtractCandidatesHandler(
        builder=builder,
        client=client,
        parser=parser,
        default_mode=settings.model_default_mode,
    )

    cosmos = get_cosmos_client(settings, credential)
    db = cosmos.get_database_client(settings.cosmos_database)
    sb_client = get_servicebus_client(settings, credential)

    candidate_repo = CandidateRepository(db.get_container_client(settings.candidates_container))
    batches_container = db.get_container_client(settings.batches_container)
    gate = TerminalBatchGate(batches_container, settings.batch_terminal_cache_ttl_seconds)
    event_publisher = EventPublisher(sb_client)
    store_handler = StoreExtractionHandler(
        deps=StoreExtractionDeps(
            candidate_repo=candidate_repo,
            event_publisher=event_publisher,
            extraction_completed_topic=settings.extraction_completed_topic,
        )
    )

    app.state.settings = settings
    app.state.handler = handler
    app.state.store_extraction_handler = store_handler

    document_reader = DocumentSourceReader(db.get_container_client(settings.documents_container))
    chunk_reader = ChunkReader(
        db.get_container_client(settings.chunks_container), settings.max_excerpt_chars
    )
    process_handler = ProcessExtractionRequestHandler(
        deps=ProcessExtractionDeps(
            candidate_repo=candidate_repo,
            document_reader=document_reader,
            chunk_reader=chunk_reader,
            extract_handler=handler,
            store_handler=store_handler,
            event_publisher=event_publisher,
            extraction_failed_topic=settings.extraction_failed_topic,
            concurrency=settings.extraction_concurrency,
        )
    )
    consumer_config = ServiceBusConsumerConfig(
        topic=settings.extraction_requested_topic,
        subscription=settings.extraction_subscription,
        max_attempts=settings.consumer_max_attempts,
        session_idle_timeout=settings.consumer_session_idle_timeout,
        session_lock_renewal_seconds=settings.session_lock_renewal_seconds,
    )
    consumer = ServiceBusConsumer(
        client=sb_client,
        handler=process_handler,
        config=consumer_config,
        gate=gate,
    )

    health = ConsumerHealth()
    app.state.consumer_health = health

    cancel_event = asyncio.Event()
    consumer_task = asyncio.create_task(consumer.run(cancel_event))
    health.mark_alive()
    supervise(consumer_task, health, logger)

    logger.info("Extraction service started")
    yield

    cancel_event.set()
    consumer_task.cancel()
    try:
        await consumer_task
    except asyncio.CancelledError:
        pass

    await http_client.aclose()
    await cosmos.close()
    await sb_client.close()
    await credential.close()
    logger.info("Extraction service stopped")


def create_app() -> FastAPI:
    app = FastAPI(title="extraction", version="0.1.0", lifespan=lifespan)

    @app.get("/health", response_model=None)
    async def health() -> dict[str, str] | Response:
        consumer_health = getattr(app.state, "consumer_health", None)
        if consumer_health is None or consumer_health.is_serving:
            return {"status": "ok"}
        return JSONResponse(status_code=503, content={"status": "consumer_dead"})

    app.include_router(extraction_router)
    return app


app = create_app()
