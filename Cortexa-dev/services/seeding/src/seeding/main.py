import asyncio
import logging
from collections.abc import AsyncIterator
from contextlib import asynccontextmanager
from dataclasses import dataclass
from typing import NamedTuple

from azure.identity.aio import DefaultAzureCredential
from azure.servicebus.aio import ServiceBusClient
from fastapi import FastAPI
from fastapi.responses import JSONResponse, Response

from seeding.api.routes.seeding_routes import router as seeding_router
from seeding.application.handlers.embed_asset_chunks_handler import (
    EmbedAssetChunksDeps,
    EmbedAssetChunksHandler,
)
from seeding.application.handlers.generate_claim_seeds_handler import GenerateClaimSeedsDeps
from seeding.application.handlers.generate_digest_handler import (
    GenerateDigestDeps,
    GenerateDigestHandler,
)
from seeding.application.handlers.generate_idf_handler import GenerateIdfDeps
from seeding.application.handlers.generate_landscape_handler import (
    GenerateLandscapeDeps,
    GenerateLandscapeHandler,
)
from seeding.application.handlers.generate_lattice_handler import GenerateLatticeDeps
from seeding.application.handlers.generate_map_handler import GenerateMapDeps
from seeding.application.handlers.process_seeding_report_request_handler import (
    ProcessSeedingReportRequestDeps,
    ProcessSeedingReportRequestHandler,
)
from seeding.application.handlers.process_seeding_request_handler import (
    ProcessSeedingRequestDeps,
    ProcessSeedingRequestHandler,
)
from seeding.domain.events.asset_embedding import AssetEmbeddingEnvelope
from seeding.domain.events.digest import DigestEnvelope
from seeding.domain.events.landscape import LandscapeEnvelope
from seeding.infrastructure.clients.evidence_client import EvidenceClient
from seeding.infrastructure.clients.model_router_client import ModelRouterClient, RetryConfig
from seeding.infrastructure.clients.service_bus_publisher import ServiceBusPublisher
from seeding.infrastructure.clients.vector_router_client import VectorRouterClient
from seeding.infrastructure.config.settings import SeedingSettings
from seeding.infrastructure.cosmos.batch_state_gate import TerminalBatchGate
from seeding.infrastructure.cosmos.candidate_read_repository import CosmosCandidateReadRepository
from seeding.infrastructure.cosmos.candidate_write_repository import CosmosCandidateWriteRepository
from seeding.infrastructure.cosmos.chunk_read_repository import CosmosChunkReadRepository
from seeding.infrastructure.cosmos.cosmos_client import get_cosmos_client
from seeding.infrastructure.cosmos.digest_repository import DigestRepository
from seeding.infrastructure.cosmos.evidence_bundle_read_repository import (
    CosmosEvidenceBundleReadRepository,
)
from seeding.infrastructure.cosmos.landscape_repository import LandscapeRepository
from seeding.infrastructure.cosmos.scratchpad_repository import ScratchpadRepository
from seeding.infrastructure.cosmos.seeding_report_repository import SeedingReportRepository
from seeding.infrastructure.cosmos.seeding_repository import SeedingRepository
from seeding.infrastructure.cosmos.verdict_read_repository import CosmosVerdictReadRepository
from seeding.infrastructure.observability.consumer_supervisor import (
    ConsumerHealth,
    configure_logging,
    supervise,
)
from seeding.infrastructure.observability.sentry import configure_sentry
from seeding.infrastructure.servicebus.servicebus_client import get_servicebus_client
from seeding.infrastructure.servicebus.servicebus_consumer import (
    ServiceBusConsumer,
    _decode_body,
)


def _parse_asset_embedding_envelope(message) -> AssetEmbeddingEnvelope:
    return AssetEmbeddingEnvelope.model_validate_json(_decode_body(message))


def _parse_digest_envelope(message) -> DigestEnvelope:
    return DigestEnvelope.model_validate_json(_decode_body(message))


def _parse_landscape_envelope(message) -> LandscapeEnvelope:
    return LandscapeEnvelope.model_validate_json(_decode_body(message))


configure_logging()
_logger = logging.getLogger(__name__)


class _NoopPublisher:
    async def publish(
        self,
        topic: str,
        payload: dict,
        session_id: str | None = None,
        correlation_id: str | None = None,
    ) -> None:
        _logger.info("local_dev: skipping Service Bus publish to topic %s", topic)


class _CosmosRepos(NamedTuple):
    seeding: SeedingRepository
    report: SeedingReportRepository
    candidates: CosmosCandidateReadRepository
    candidate_writes: CosmosCandidateWriteRepository
    verdicts: CosmosVerdictReadRepository
    evidence_bundles: CosmosEvidenceBundleReadRepository
    chunks: CosmosChunkReadRepository
    digest: DigestRepository
    landscape: LandscapeRepository
    scratchpad: ScratchpadRepository


def _build_cosmos_repos(db, settings: SeedingSettings) -> _CosmosRepos:
    candidates_container = db.get_container_client(settings.cosmos_container_candidates)
    return _CosmosRepos(
        seeding=SeedingRepository(db.get_container_client(settings.seeding_container)),
        report=SeedingReportRepository(db.get_container_client(settings.cosmos_container_reports)),
        candidates=CosmosCandidateReadRepository(candidates_container),
        candidate_writes=CosmosCandidateWriteRepository(candidates_container),
        verdicts=CosmosVerdictReadRepository(
            db.get_container_client(settings.cosmos_container_verdicts)
        ),
        evidence_bundles=CosmosEvidenceBundleReadRepository(
            db.get_container_client(settings.cosmos_container_evidence_bundles)
        ),
        chunks=CosmosChunkReadRepository(db.get_container_client(settings.chunks_container)),
        digest=DigestRepository(db.get_container_client(settings.seeding_container)),
        landscape=LandscapeRepository(db.get_container_client(settings.seeding_container)),
        scratchpad=ScratchpadRepository(db.get_container_client(settings.seeding_container)),
    )


def _build_publisher(
    settings: SeedingSettings, credential: DefaultAzureCredential
) -> tuple[_NoopPublisher | ServiceBusPublisher, ServiceBusClient | None]:
    if settings.local_dev:
        return _NoopPublisher(), None
    sb_client = get_servicebus_client(settings, credential)
    return ServiceBusPublisher(sb_client), sb_client


def _build_model_router(settings: SeedingSettings) -> ModelRouterClient:
    retry_config = RetryConfig(
        max_retries=settings.model_router_max_retries,
        backoff_max_seconds=settings.model_router_backoff_max_seconds,
        honor_retry_after=settings.model_router_honor_retry_after,
        total_retry_budget_seconds=settings.model_router_total_retry_budget_seconds,
    )
    return ModelRouterClient(
        base_url=settings.model_router_url,
        task_kind=settings.seeding_task_kind,
        timeout=settings.model_router_timeout_seconds,
        retry_config=retry_config,
        max_output_tokens=settings.seeding_max_output_tokens,
    )


def _build_consumer(
    settings: SeedingSettings,
    sb_client: ServiceBusClient,
    handler: ProcessSeedingRequestHandler,
    publisher: _NoopPublisher | ServiceBusPublisher,
    gate,
) -> ServiceBusConsumer:
    return ServiceBusConsumer(
        client=sb_client,
        handler=handler,
        topic=settings.seeding_requested_topic,
        subscription=settings.seeding_subscription,
        max_attempts=settings.consumer_max_attempts,
        publisher=publisher,
        failed_topic=settings.seeding_failed_topic,
        session_idle_timeout=settings.consumer_session_idle_timeout,
        session_lock_renewal_seconds=settings.consumer_session_lock_renewal_seconds,
        gate=gate,
    )


def _build_digest_model_router(settings: SeedingSettings) -> ModelRouterClient:
    retry_config = RetryConfig(
        max_retries=settings.model_router_max_retries,
        backoff_max_seconds=settings.model_router_backoff_max_seconds,
        honor_retry_after=settings.model_router_honor_retry_after,
        total_retry_budget_seconds=settings.model_router_total_retry_budget_seconds,
    )
    return ModelRouterClient(
        base_url=settings.model_router_url,
        task_kind=settings.digest_task_kind,
        timeout=settings.model_router_timeout_seconds,
        retry_config=retry_config,
        max_output_tokens=settings.digest_max_output_tokens,
    )


def _build_ideation_model_router(settings: SeedingSettings) -> ModelRouterClient:
    retry_config = RetryConfig(
        max_retries=settings.model_router_max_retries,
        backoff_max_seconds=settings.model_router_backoff_max_seconds,
        honor_retry_after=settings.model_router_honor_retry_after,
        total_retry_budget_seconds=settings.model_router_total_retry_budget_seconds,
    )
    return ModelRouterClient(
        base_url=settings.model_router_url,
        task_kind=settings.ideation_task_kind,
        timeout=settings.model_router_timeout_seconds,
        retry_config=retry_config,
        max_output_tokens=settings.ideation_max_output_tokens,
    )


def _build_vector_router(settings: SeedingSettings) -> VectorRouterClient:
    return VectorRouterClient(
        base_url=settings.vector_router_url,
        timeout=settings.vector_router_timeout_seconds,
    )


def _build_evidence_client(settings: SeedingSettings) -> EvidenceClient:
    return EvidenceClient(
        base_url=settings.evidence_url,
        timeout=settings.evidence_timeout_seconds,
    )


def _build_asset_handler(
    settings: SeedingSettings,
    repos: _CosmosRepos,
    vector_client: VectorRouterClient,
    publisher: _NoopPublisher | ServiceBusPublisher,
) -> EmbedAssetChunksHandler:
    deps = EmbedAssetChunksDeps(
        chunk_repo=repos.chunks,
        vector_client=vector_client,
        publisher=publisher,
        settings=settings,
    )
    return EmbedAssetChunksHandler(deps)


def _build_asset_consumer(
    settings: SeedingSettings,
    sb_client: ServiceBusClient,
    handler: EmbedAssetChunksHandler,
    publisher: _NoopPublisher | ServiceBusPublisher,
    gate,
) -> ServiceBusConsumer:
    return ServiceBusConsumer(
        client=sb_client,
        handler=handler,
        topic=settings.asset_embedding_requested_topic,
        subscription=settings.asset_embedding_subscription,
        max_attempts=settings.consumer_max_attempts,
        publisher=publisher,
        failed_topic=settings.seeding_failed_topic,
        session_idle_timeout=settings.consumer_session_idle_timeout,
        session_lock_renewal_seconds=settings.consumer_session_lock_renewal_seconds,
        gate=gate,
        envelope_parser=_parse_asset_embedding_envelope,
    )


def _build_digest_handler(
    settings: SeedingSettings,
    repos: _CosmosRepos,
    digest_router: ModelRouterClient,
    publisher: _NoopPublisher | ServiceBusPublisher,
) -> GenerateDigestHandler:
    deps = GenerateDigestDeps(
        chunk_repo=repos.chunks,
        digest_repo=repos.digest,
        client=digest_router,
        publisher=publisher,
        settings=settings,
    )
    return GenerateDigestHandler(deps)


def _build_digest_consumer(
    settings: SeedingSettings,
    sb_client: ServiceBusClient,
    handler: GenerateDigestHandler,
    publisher: _NoopPublisher | ServiceBusPublisher,
    gate,
) -> ServiceBusConsumer:
    return ServiceBusConsumer(
        client=sb_client,
        handler=handler,
        topic=settings.digest_requested_topic,
        subscription=settings.digest_subscription,
        max_attempts=settings.consumer_max_attempts,
        publisher=publisher,
        failed_topic=settings.seeding_failed_topic,
        session_idle_timeout=settings.consumer_session_idle_timeout,
        session_lock_renewal_seconds=settings.consumer_session_lock_renewal_seconds,
        gate=gate,
        envelope_parser=_parse_digest_envelope,
    )


def _build_landscape_handler(
    settings: SeedingSettings,
    repos: _CosmosRepos,
    vector_client: VectorRouterClient,
    evidence_client: EvidenceClient,
    publisher: _NoopPublisher | ServiceBusPublisher,
) -> GenerateLandscapeHandler:
    deps = GenerateLandscapeDeps(
        digest_repo=repos.digest,
        landscape_repo=repos.landscape,
        vector_client=vector_client,
        evidence_client=evidence_client,
        publisher=publisher,
        settings=settings,
    )
    return GenerateLandscapeHandler(deps)


def _build_landscape_consumer(
    settings: SeedingSettings,
    sb_client: ServiceBusClient,
    handler: GenerateLandscapeHandler,
    publisher: _NoopPublisher | ServiceBusPublisher,
    gate,
) -> ServiceBusConsumer:
    return ServiceBusConsumer(
        client=sb_client,
        handler=handler,
        topic=settings.landscape_requested_topic,
        subscription=settings.landscape_subscription,
        max_attempts=settings.consumer_max_attempts,
        publisher=publisher,
        failed_topic=settings.seeding_failed_topic,
        session_idle_timeout=settings.consumer_session_idle_timeout,
        session_lock_renewal_seconds=settings.consumer_session_lock_renewal_seconds,
        gate=gate,
        envelope_parser=_parse_landscape_envelope,
    )


class _LatticeWiring(NamedTuple):
    publisher: _NoopPublisher | ServiceBusPublisher
    settings: SeedingSettings
    seeding_repo: SeedingRepository


def _wire_rest_deps(app: FastAPI, model_router: ModelRouterClient, lattice: _LatticeWiring) -> None:
    app.state.generate_map_deps = GenerateMapDeps(client=model_router)
    app.state.generate_idf_deps = GenerateIdfDeps(client=model_router)
    app.state.generate_claim_seeds_deps = GenerateClaimSeedsDeps(client=model_router)
    app.state.generate_lattice_deps = GenerateLatticeDeps(
        client=model_router,
        publisher=lattice.publisher,
        settings=lattice.settings,
        seeding_repository=lattice.seeding_repo,
    )
    app.state.seeding_repository = lattice.seeding_repo


@dataclass
class _ProcessHandlerClients:
    model_router: ModelRouterClient
    vector_router: VectorRouterClient
    ideation_router: ModelRouterClient


def _build_process_handler(
    settings: SeedingSettings,
    repos: _CosmosRepos,
    publisher: _NoopPublisher | ServiceBusPublisher,
    clients: _ProcessHandlerClients,
) -> ProcessSeedingRequestHandler:
    deps = ProcessSeedingRequestDeps(
        candidate_repo=repos.candidates,
        report_repo=repos.report,
        landscape_repo=repos.landscape,
        publisher=publisher,
        client=clients.model_router,
        settings=settings,
        chunk_repo=repos.chunks,
        digest_repo=repos.digest,
        scratchpad_repo=repos.scratchpad,
        vector_client=clients.vector_router,
        ideation_client=clients.ideation_router,
        candidate_write_repo=repos.candidate_writes,
    )
    return ProcessSeedingRequestHandler(deps)


def _build_report_handler(
    settings: SeedingSettings,
    repos: _CosmosRepos,
    publisher: _NoopPublisher | ServiceBusPublisher,
) -> ProcessSeedingReportRequestHandler:
    deps = ProcessSeedingReportRequestDeps(
        candidate_repo=repos.candidates,
        verdict_repo=repos.verdicts,
        evidence_repo=repos.evidence_bundles,
        report_repo=repos.report,
        landscape_repo=repos.landscape,
        publisher=publisher,
        settings=settings,
    )
    return ProcessSeedingReportRequestHandler(deps)


def _build_report_consumer(
    settings: SeedingSettings,
    sb_client: ServiceBusClient,
    handler: ProcessSeedingReportRequestHandler,
    publisher: _NoopPublisher | ServiceBusPublisher,
    gate,
) -> ServiceBusConsumer:
    return ServiceBusConsumer(
        client=sb_client,
        handler=handler,
        topic=settings.seeding_report_requested_topic,
        subscription=settings.seeding_report_subscription,
        max_attempts=settings.consumer_max_attempts,
        publisher=publisher,
        failed_topic=settings.seeding_failed_topic,
        session_idle_timeout=settings.consumer_session_idle_timeout,
        session_lock_renewal_seconds=settings.consumer_session_lock_renewal_seconds,
        gate=gate,
    )


class _RunningConsumer(NamedTuple):
    cancel_event: asyncio.Event
    task: asyncio.Task


def _start_consumer(consumer: ServiceBusConsumer, health: ConsumerHealth) -> _RunningConsumer:
    cancel_event = asyncio.Event()
    task = asyncio.create_task(consumer.run(cancel_event))
    supervise(task, health, _logger)
    return _RunningConsumer(cancel_event, task)


async def _shutdown_consumer(running: _RunningConsumer) -> None:
    running.cancel_event.set()
    running.task.cancel()
    try:
        await running.task
    except asyncio.CancelledError:
        pass


@asynccontextmanager
async def _lifespan(app: FastAPI) -> AsyncIterator[None]:
    settings = SeedingSettings()
    configure_sentry(settings)
    credential = DefaultAzureCredential()
    model_router = _build_model_router(settings)
    digest_router = _build_digest_model_router(settings)
    ideation_router = _build_ideation_model_router(settings)
    vector_router = _build_vector_router(settings)
    evidence_client = _build_evidence_client(settings)

    cosmos_client = get_cosmos_client(settings, credential)
    db = cosmos_client.get_database_client(settings.cosmos_database)
    repos = _build_cosmos_repos(db, settings)

    publisher, sb_client = _build_publisher(settings, credential)

    _wire_rest_deps(app, model_router, _LatticeWiring(publisher, settings, repos.seeding))
    app.state.seeding_report_repository = repos.report

    health = ConsumerHealth()
    app.state.consumer_health = health

    running: list[_RunningConsumer] = []
    if not settings.local_dev:
        batches_container = db.get_container_client(settings.batches_container)
        gate = TerminalBatchGate(batches_container, settings.batch_terminal_cache_ttl_seconds)
        process_clients = _ProcessHandlerClients(
            model_router=model_router,
            vector_router=vector_router,
            ideation_router=ideation_router,
        )
        handler = _build_process_handler(settings, repos, publisher, process_clients)
        asset_handler = _build_asset_handler(settings, repos, vector_router, publisher)
        digest_handler = _build_digest_handler(settings, repos, digest_router, publisher)
        landscape_handler = _build_landscape_handler(
            settings, repos, vector_router, evidence_client, publisher
        )
        report_handler = _build_report_handler(settings, repos, publisher)
        health.mark_alive()
        running = [
            _start_consumer(_build_consumer(settings, sb_client, handler, publisher, gate), health),
            _start_consumer(
                _build_asset_consumer(settings, sb_client, asset_handler, publisher, gate), health
            ),
            _start_consumer(
                _build_digest_consumer(settings, sb_client, digest_handler, publisher, gate), health
            ),
            _start_consumer(
                _build_landscape_consumer(settings, sb_client, landscape_handler, publisher, gate),
                health,
            ),
            _start_consumer(
                _build_report_consumer(settings, sb_client, report_handler, publisher, gate),
                health,
            ),
        ]
    else:
        health.mark_alive()

    _logger.info("Seeding service started")
    yield

    for consumer in running:
        await _shutdown_consumer(consumer)
    if sb_client is not None:
        await sb_client.close()
    await model_router.aclose()
    await digest_router.aclose()
    await ideation_router.aclose()
    await vector_router.aclose()
    await evidence_client.aclose()
    await cosmos_client.close()
    await credential.close()
    _logger.info("Seeding service stopped")


def create_app() -> FastAPI:
    app = FastAPI(title="Seeding Service", lifespan=_lifespan)
    app.include_router(seeding_router, prefix="/seeding")

    @app.get("/health", response_model=None)
    async def health() -> dict | Response:
        consumer_health = getattr(app.state, "consumer_health", None)
        if consumer_health is None or consumer_health.is_serving:
            return {"status": "ok"}
        return JSONResponse(status_code=503, content={"status": "consumer_dead"})

    return app


app = create_app()
