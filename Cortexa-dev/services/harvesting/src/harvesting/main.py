import asyncio
import logging
from collections.abc import AsyncIterator
from contextlib import asynccontextmanager
from typing import NamedTuple

from azure.identity.aio import DefaultAzureCredential
from azure.servicebus.aio import ServiceBusClient
from fastapi import FastAPI
from fastapi.responses import JSONResponse, Response

from harvesting.api.routes.harvesting_routes import router as harvesting_router
from harvesting.application.candidate_assembler import ChunkGeometryRepos
from harvesting.application.handlers.assemble_handler import AssembleDeps, AssembleHandler
from harvesting.application.handlers.classify_handler import ClassifyDeps
from harvesting.application.handlers.get_results_handler import GetResultsDeps
from harvesting.application.handlers.process_harvesting_request_handler import (
    ProcessHarvestingRequestDeps,
    ProcessHarvestingRequestHandler,
)
from harvesting.application.handlers.rank_handler import RankDeps
from harvesting.domain.models.engine_completed_event import EngineCompletedEvent
from harvesting.domain.models.rank_weights import RankWeights
from harvesting.infrastructure.config.settings import HarvestingSettings
from harvesting.infrastructure.cosmos.batch_state_gate import TerminalBatchGate
from harvesting.infrastructure.cosmos.candidate_read_repository import CosmosCandidateReadRepository
from harvesting.infrastructure.cosmos.chunk_read_repository import CosmosChunkReadRepository
from harvesting.infrastructure.cosmos.cosmos_client import get_cosmos_client
from harvesting.infrastructure.cosmos.document_read_repository import CosmosDocumentReadRepository
from harvesting.infrastructure.cosmos.evidence_bundle_read_repository import (
    CosmosEvidenceBundleReadRepository,
)
from harvesting.infrastructure.cosmos.maturity_repository import CosmosMaturityRepository
from harvesting.infrastructure.cosmos.provenance_entry_read_repository import (
    CosmosProvenanceEntryReadRepository,
)
from harvesting.infrastructure.cosmos.report_repository import CosmosReportRepository
from harvesting.infrastructure.cosmos.verdict_read_repository import CosmosVerdictReadRepository
from harvesting.infrastructure.observability.consumer_supervisor import (
    ConsumerHealth,
    configure_logging,
    supervise,
)
from harvesting.infrastructure.observability.sentry import configure_sentry
from harvesting.infrastructure.servicebus.event_publisher import (
    ServiceBusEventPublisher,
    ServiceBusFailedEventPublisher,
)
from harvesting.infrastructure.servicebus.servicebus_client import get_servicebus_client
from harvesting.infrastructure.servicebus.servicebus_consumer import ServiceBusConsumer

configure_logging()
_logger = logging.getLogger(__name__)


class _NoopPublisher:
    async def publish(self, event: EngineCompletedEvent) -> None:
        _logger.info("local_dev: skipping Service Bus publish for %s", event.event_type)


class _CosmosRepos(NamedTuple):
    report: CosmosReportRepository
    verdicts: CosmosVerdictReadRepository
    candidates: CosmosCandidateReadRepository
    maturity: CosmosMaturityRepository
    evidence: CosmosEvidenceBundleReadRepository
    chunks: CosmosChunkReadRepository
    documents: CosmosDocumentReadRepository
    provenance: CosmosProvenanceEntryReadRepository


class _ProcessHandlerConfig(NamedTuple):
    settings: HarvestingSettings
    rank_weights: RankWeights


class _Publishers(NamedTuple):
    engine_completed: _NoopPublisher | ServiceBusEventPublisher
    failed: ServiceBusFailedEventPublisher


def _build_rank_weights(settings: HarvestingSettings) -> RankWeights:
    return RankWeights(
        novelty=settings.ranker_weight_novelty,
        feasibility=settings.ranker_weight_feasibility,
        strategic=settings.ranker_weight_strategic,
        patentability=settings.ranker_weight_patentability,
    )


def _build_cosmos_repos(db, settings: HarvestingSettings) -> _CosmosRepos:
    return _CosmosRepos(
        report=CosmosReportRepository(
            db.get_container_client(settings.cosmos_container_reports),
            write_concurrency=settings.cosmos_report_write_concurrency,
        ),
        verdicts=CosmosVerdictReadRepository(
            db.get_container_client(settings.cosmos_container_verdicts)
        ),
        candidates=CosmosCandidateReadRepository(
            db.get_container_client(settings.cosmos_container_candidates)
        ),
        maturity=CosmosMaturityRepository(
            db.get_container_client(settings.cosmos_container_harvesting)
        ),
        evidence=CosmosEvidenceBundleReadRepository(
            db.get_container_client(settings.cosmos_container_evidence)
        ),
        chunks=CosmosChunkReadRepository(db.get_container_client(settings.cosmos_container_chunks)),
        documents=CosmosDocumentReadRepository(
            db.get_container_client(settings.cosmos_container_documents)
        ),
        provenance=CosmosProvenanceEntryReadRepository(
            db.get_container_client(settings.cosmos_container_provenance)
        ),
    )


def _build_publisher(
    settings: HarvestingSettings, credential: DefaultAzureCredential
) -> tuple[_NoopPublisher | ServiceBusEventPublisher, ServiceBusClient | None]:
    if settings.local_dev:
        return _NoopPublisher(), None
    sb_client = get_servicebus_client(settings, credential)
    publisher = ServiceBusEventPublisher(sb_client, settings.servicebus_topic_engine_completed)
    return publisher, sb_client


def _build_assemble_deps(
    report_repo: CosmosReportRepository, publisher: _NoopPublisher | ServiceBusEventPublisher
) -> AssembleDeps:
    return AssembleDeps(report_repo=report_repo, publisher=publisher)


def _build_process_handler(
    config: _ProcessHandlerConfig,
    repos: _CosmosRepos,
    publishers: _Publishers,
    assemble_handler: AssembleHandler,
) -> ProcessHarvestingRequestHandler:
    deps = ProcessHarvestingRequestDeps(
        candidate_repo=repos.candidates,
        verdict_repo=repos.verdicts,
        evidence_repo=repos.evidence,
        report_repo=repos.report,
        publisher=publishers.engine_completed,
        failed_publisher=publishers.failed,
        assemble_handler=assemble_handler,
        weights=config.rank_weights,
        settings=config.settings,
        geometry_repos=ChunkGeometryRepos(
            chunk_repo=repos.chunks,
            document_repo=repos.documents,
            provenance_repo=repos.provenance,
        ),
    )
    return ProcessHarvestingRequestHandler(deps)


def _build_consumer(
    settings: HarvestingSettings,
    sb_client: ServiceBusClient,
    handler: ProcessHarvestingRequestHandler,
    failed_publisher: ServiceBusFailedEventPublisher,
    gate: TerminalBatchGate,
) -> ServiceBusConsumer:
    return ServiceBusConsumer(
        client=sb_client,
        handler=handler,
        topic=settings.harvesting_requested_topic,
        subscription=settings.harvesting_subscription,
        max_attempts=settings.consumer_max_attempts,
        failed_publisher=failed_publisher,
        failed_topic=settings.harvesting_failed_topic,
        session_idle_timeout=settings.consumer_session_idle_timeout,
        session_lock_renewal_seconds=settings.consumer_session_lock_renewal_seconds,
        gate=gate,
    )


async def _shutdown_consumer(
    cancel_event: asyncio.Event | None, consumer_task: asyncio.Task | None
) -> None:
    if cancel_event is not None:
        cancel_event.set()
    if consumer_task is not None:
        consumer_task.cancel()
        try:
            await consumer_task
        except asyncio.CancelledError:
            pass


@asynccontextmanager
async def _lifespan(app: FastAPI) -> AsyncIterator[None]:
    settings = HarvestingSettings()
    configure_sentry(settings)
    rank_weights = _build_rank_weights(settings)
    credential = DefaultAzureCredential()

    cosmos_client = get_cosmos_client(settings, credential)
    db = cosmos_client.get_database_client(settings.cosmos_database)
    repos = _build_cosmos_repos(db, settings)

    publisher, sb_client = _build_publisher(settings, credential)
    assemble_deps = _build_assemble_deps(repos.report, publisher)
    assemble_handler = AssembleHandler(assemble_deps)

    app.state.classify_deps = ClassifyDeps(repository=repos.maturity, settings=settings)
    app.state.rank_deps = RankDeps(weights=rank_weights)
    app.state.assemble_deps = assemble_deps
    app.state.get_results_deps = GetResultsDeps(
        report_repo=repos.report, verdicts_repo=repos.verdicts
    )

    health = ConsumerHealth()
    app.state.consumer_health = health

    cancel_event: asyncio.Event | None = None
    consumer_task: asyncio.Task | None = None
    if not settings.local_dev:
        batches_container = db.get_container_client(settings.batches_container)
        gate = TerminalBatchGate(batches_container, settings.batch_terminal_cache_ttl_seconds)
        failed_publisher = ServiceBusFailedEventPublisher(sb_client)
        handler = _build_process_handler(
            _ProcessHandlerConfig(settings, rank_weights),
            repos,
            _Publishers(engine_completed=publisher, failed=failed_publisher),
            assemble_handler,
        )
        consumer = _build_consumer(settings, sb_client, handler, failed_publisher, gate)
        cancel_event = asyncio.Event()
        consumer_task = asyncio.create_task(consumer.run(cancel_event))
        health.mark_alive()
        supervise(consumer_task, health, _logger)
    else:
        health.mark_alive()

    _logger.info("Harvesting service started")
    yield

    await _shutdown_consumer(cancel_event, consumer_task)
    if sb_client is not None:
        await sb_client.close()
    await cosmos_client.close()
    await credential.close()
    _logger.info("Harvesting service stopped")


def create_app() -> FastAPI:
    app = FastAPI(title="Harvesting Service", lifespan=_lifespan)
    app.include_router(harvesting_router, prefix="/harvesting")

    @app.get("/health", response_model=None)
    async def health() -> dict | Response:
        consumer_health = getattr(app.state, "consumer_health", None)
        if consumer_health is None or consumer_health.is_serving:
            return {"status": "ok"}
        return JSONResponse(status_code=503, content={"status": "consumer_dead"})

    return app


app = create_app()
