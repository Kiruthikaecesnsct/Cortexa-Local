import asyncio
import logging
from collections.abc import AsyncIterator
from contextlib import asynccontextmanager
from typing import NamedTuple

import httpx
from azure.identity.aio import DefaultAzureCredential
from fastapi import FastAPI
from fastapi.responses import JSONResponse, Response

from evidence.api.corpus_routes import router as corpus_router
from evidence.api.patent_search_routes import router as patent_search_router
from evidence.api.routes import router
from evidence.application.concurrency.evidence_scheduler import EvidenceScheduler
from evidence.application.handlers.load_corpus_handler import LoadCorpusDeps
from evidence.application.handlers.process_evidence_request_handler import (
    ProcessEvidenceRequestDeps,
    ProcessEvidenceRequestHandler,
)
from evidence.application.handlers.store_evidence_handler import (
    StoreEvidenceDeps,
    StoreEvidenceHandler,
)
from evidence.application.triangulation_service import TriangulationService
from evidence.domain.enums.patent_source_name import PatentSourceName
from evidence.infrastructure.config.patent_config_provider import PatentConfigProvider
from evidence.infrastructure.config.secrets_provider import SecretsProvider
from evidence.infrastructure.config.settings import EvidenceSettings
from evidence.infrastructure.corpus.corpus_adapter import CorpusAdapter
from evidence.infrastructure.corpus.corpus_file_reader import CorpusFileReader
from evidence.infrastructure.corpus.corpus_loader_adapter import CorpusLoaderAdapter
from evidence.infrastructure.cosmos.batch_state_gate import TerminalBatchGate
from evidence.infrastructure.cosmos.candidate_repository import CosmosCandidateRepository
from evidence.infrastructure.cosmos.cosmos_client import get_cosmos_client
from evidence.infrastructure.cosmos.evidence_bundle_repository import CosmosEvidenceBundleRepository
from evidence.infrastructure.llm_research.llm_research_adapter import LlmResearchAdapter
from evidence.infrastructure.observability.consumer_supervisor import (
    ConsumerHealth,
    configure_logging,
    supervise,
)
from evidence.infrastructure.observability.sentry import configure_sentry
from evidence.infrastructure.patent_apis.composite_patent_adapter import CompositePatentAdapter
from evidence.infrastructure.patent_apis.epo_adapter import EpoAdapter
from evidence.infrastructure.patent_apis.lens_adapter import LensAdapter
from evidence.infrastructure.patent_apis.uspto_adapter import UsptoAdapter
from evidence.infrastructure.servicebus.event_publisher import ServiceBusEventPublisher
from evidence.infrastructure.servicebus.servicebus_client import get_servicebus_client
from evidence.infrastructure.servicebus.servicebus_consumer import ServiceBusConsumer

configure_logging()
_logger = logging.getLogger(__name__)

_SOURCE_SECRET_NAMES: dict[PatentSourceName, tuple[str, ...]] = {
    PatentSourceName.USPTO: ("USPTO_API_KEY",),
    PatentSourceName.EPO: ("EPO_CONSUMER_KEY", "EPO_OAUTH_SECRET"),
    PatentSourceName.Lens: ("LENS_API_KEY",),
}


def _secrets_for_enabled_sources(enabled: frozenset[PatentSourceName]) -> tuple[str, ...]:
    return tuple(
        name
        for source in (PatentSourceName.USPTO, PatentSourceName.EPO, PatentSourceName.Lens)
        if source in enabled
        for name in _SOURCE_SECRET_NAMES[source]
    )


async def _prefetch_patent_secrets(
    patent_config_provider: PatentConfigProvider, secrets: SecretsProvider
) -> None:
    config = await patent_config_provider.get_config()
    secret_names = _secrets_for_enabled_sources(config.enabled)
    results = await asyncio.gather(
        *(secrets.get_secret(name) for name in secret_names), return_exceptions=True
    )
    for name, result in zip(secret_names, results, strict=False):
        if isinstance(result, BaseException):
            _logger.warning("patent_secret_prefetch_failed name=%s error=%s", name, result)


class HttpClients(NamedTuple):
    uspto: httpx.AsyncClient
    epo: httpx.AsyncClient
    lens: httpx.AsyncClient
    corpus: httpx.AsyncClient
    llm: httpx.AsyncClient


def _build_http_clients(settings: EvidenceSettings) -> HttpClients:
    patent_timeout = httpx.Timeout(settings.patent_api_timeout_seconds)
    corpus_timeout = httpx.Timeout(settings.corpus_search_timeout_seconds)
    llm_timeout = httpx.Timeout(settings.llm_research_timeout_seconds)
    return HttpClients(
        uspto=httpx.AsyncClient(timeout=patent_timeout),
        epo=httpx.AsyncClient(timeout=patent_timeout),
        lens=httpx.AsyncClient(timeout=patent_timeout),
        corpus=httpx.AsyncClient(timeout=corpus_timeout),
        llm=httpx.AsyncClient(timeout=llm_timeout),
    )


def _build_patent_adapter(
    settings: EvidenceSettings, secrets: SecretsProvider, http_clients: tuple
) -> CompositePatentAdapter:
    uspto_http, epo_http, lens_http = http_clients
    return CompositePatentAdapter(
        UsptoAdapter(settings, secrets, client=uspto_http),
        EpoAdapter(settings, secrets, client=epo_http),
        LensAdapter(settings, secrets, client=lens_http),
    )


def _build_process_handler(
    settings: EvidenceSettings,
    bundle_repo: CosmosEvidenceBundleRepository,
    candidate_repo: CosmosCandidateRepository,
    publisher: ServiceBusEventPublisher,
    triangulation: TriangulationService,
    store_handler: StoreEvidenceHandler,
) -> ProcessEvidenceRequestHandler:
    return ProcessEvidenceRequestHandler(
        ProcessEvidenceRequestDeps(
            repository=bundle_repo,
            publisher=publisher,
            triangulation=triangulation,
            store_handler=store_handler,
            candidate_reader=candidate_repo,
            settings=settings,
        )
    )


def _build_cosmos_containers(cosmos_client, settings: EvidenceSettings):
    db = cosmos_client.get_database_client(settings.cosmos_database)
    return (
        db.get_container_client(settings.evidence_container),
        db.get_container_client(settings.candidates_container),
        db.get_container_client(settings.batches_container),
        db.get_container_client(settings.config_container),
    )


@asynccontextmanager
async def lifespan(app: FastAPI) -> AsyncIterator[None]:
    settings = EvidenceSettings()
    configure_sentry(settings)
    credential = DefaultAzureCredential()

    secrets = SecretsProvider(settings, credential)

    cosmos_client = get_cosmos_client(settings, credential)
    evidence_container, candidates_container, batches_container, config_container = (
        _build_cosmos_containers(cosmos_client, settings)
    )
    patent_config_provider = PatentConfigProvider(
        config_container, settings.patent_config_cache_ttl_seconds
    )

    await _prefetch_patent_secrets(patent_config_provider, secrets)

    clients = _build_http_clients(settings)

    patent_adapter = _build_patent_adapter(
        settings, secrets, (clients.uspto, clients.epo, clients.lens)
    )
    app.state.patent_adapter = patent_adapter

    corpus_adapter = CorpusAdapter(settings, clients.corpus)
    llm_adapter = LlmResearchAdapter(settings, clients.llm)
    scheduler = EvidenceScheduler(settings)
    triangulation = TriangulationService(
        patent_adapter=patent_adapter,
        corpus_adapter=corpus_adapter,
        llm_adapter=llm_adapter,
        settings=settings,
        scheduler=scheduler,
        patent_config_provider=patent_config_provider,
        secrets_provider=secrets,
    )

    sb_client = get_servicebus_client(settings, credential)
    bundle_repo = CosmosEvidenceBundleRepository(evidence_container)
    candidate_repo = CosmosCandidateRepository(candidates_container)
    publisher = ServiceBusEventPublisher(sb_client)
    gate = TerminalBatchGate(batches_container, settings.batch_terminal_cache_ttl_seconds)

    store_deps = StoreEvidenceDeps(repository=bundle_repo, publisher=publisher, settings=settings)
    store_handler = StoreEvidenceHandler(store_deps)
    app.state.store_evidence_deps = store_deps

    corpus_loader = CorpusLoaderAdapter(settings)
    app.state.load_corpus_deps = LoadCorpusDeps(
        loader=corpus_loader,
        reader=CorpusFileReader(settings.corpus_file_path),
        settings=settings,
    )

    process_handler = _build_process_handler(
        settings, bundle_repo, candidate_repo, publisher, triangulation, store_handler
    )
    consumer = ServiceBusConsumer(
        client=sb_client,
        handler=process_handler,
        topic=settings.evidence_requested_topic,
        subscription=settings.evidence_subscription,
        max_attempts=settings.consumer_max_attempts,
        session_idle_timeout=settings.consumer_session_idle_timeout,
        session_lock_renewal_seconds=settings.consumer_session_lock_renewal_seconds,
        max_concurrent_sessions=settings.consumer_max_concurrent_sessions,
        gate=gate,
    )

    health = ConsumerHealth()
    app.state.consumer_health = health

    cancel_event = asyncio.Event()
    consumer_task = asyncio.create_task(consumer.run(cancel_event))
    health.mark_alive()
    supervise(consumer_task, health, _logger)

    _logger.info("Evidence service started")
    yield

    cancel_event.set()
    consumer_task.cancel()
    try:
        await consumer_task
    except asyncio.CancelledError:
        pass

    await corpus_loader.aclose()
    await cosmos_client.close()
    await sb_client.close()
    await credential.close()
    await clients.uspto.aclose()
    await clients.epo.aclose()
    await clients.lens.aclose()
    await clients.corpus.aclose()
    await clients.llm.aclose()
    _logger.info("Evidence service stopped")


def create_app() -> FastAPI:
    app = FastAPI(title="Evidence Service", lifespan=lifespan)
    app.include_router(router)
    app.include_router(corpus_router)
    app.include_router(patent_search_router)

    @app.get("/health", response_model=None)
    async def health() -> dict | Response:
        consumer_health = getattr(app.state, "consumer_health", None)
        if consumer_health is None or consumer_health.is_serving:
            return {"status": "ok"}
        return JSONResponse(status_code=503, content={"status": "consumer_dead"})

    return app


app = create_app()
