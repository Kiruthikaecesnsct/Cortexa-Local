import asyncio
import logging
from collections.abc import AsyncIterator
from contextlib import asynccontextmanager

import httpx
from azure.identity.aio import DefaultAzureCredential
from fastapi import FastAPI
from fastapi.responses import JSONResponse, Response

from scoring.api.routes.scoring_routes import router as scoring_router
from scoring.application.claim_draft_generator import (
    ClaimDraftGenerator,
    ClaimDraftGeneratorOptions,
)
from scoring.application.handlers.get_verdict_handler import GetVerdictHandlerDeps
from scoring.application.handlers.process_scoring_request_handler import (
    ProcessScoringDeps,
    ProcessScoringRequestHandler,
)
from scoring.application.handlers.score_verdict_handler import ScoreVerdictDeps, ScoreVerdictHandler
from scoring.application.handlers.store_verdict_handler import StoreVerdictDeps
from scoring.infrastructure.clients.model_router_client import ModelRouterClient, RetryConfig
from scoring.infrastructure.config.settings import ScoringSettings
from scoring.infrastructure.cosmos.batch_state_gate import TerminalBatchGate
from scoring.infrastructure.cosmos.candidate_repository import CosmosCandidateRepository
from scoring.infrastructure.cosmos.cosmos_client import get_cosmos_client
from scoring.infrastructure.cosmos.evidence_bundle_repository import CosmosEvidenceBundleRepository
from scoring.infrastructure.cosmos.seeding_repository import CosmosClaimSeedRepository
from scoring.infrastructure.cosmos.verdict_repository import CosmosVerdictRepository
from scoring.infrastructure.observability.consumer_supervisor import (
    ConsumerHealth,
    configure_logging,
    supervise,
)
from scoring.infrastructure.observability.sentry import configure_sentry
from scoring.infrastructure.servicebus.event_publisher import ServiceBusEventPublisher
from scoring.infrastructure.servicebus.servicebus_client import get_servicebus_client
from scoring.infrastructure.servicebus.servicebus_consumer import ServiceBusConsumer

configure_logging()
_logger = logging.getLogger(__name__)


def _build_score_handler(
    settings: ScoringSettings,
    verdict_repository: CosmosVerdictRepository,
    publisher: ServiceBusEventPublisher,
    model_router_client: ModelRouterClient,
) -> ScoreVerdictHandler:
    return ScoreVerdictHandler(
        ScoreVerdictDeps(
            repository=verdict_repository,
            publisher=publisher,
            settings=settings,
            model_router_client=model_router_client,
        )
    )


def _build_process_handler(
    settings: ScoringSettings,
    verdict_repository: CosmosVerdictRepository,
    candidate_repository: CosmosCandidateRepository,
    evidence_repository: CosmosEvidenceBundleRepository,
    score_handler: ScoreVerdictHandler,
    publisher: ServiceBusEventPublisher,
    claim_draft_generator: ClaimDraftGenerator | None,
) -> ProcessScoringRequestHandler:
    return ProcessScoringRequestHandler(
        ProcessScoringDeps(
            verdict_repo=verdict_repository,
            candidate_reader=candidate_repository,
            bundle_reader=evidence_repository,
            score_handler=score_handler,
            publisher=publisher,
            settings=settings,
            claim_draft_generator=claim_draft_generator,
        )
    )


@asynccontextmanager
async def _lifespan(app: FastAPI) -> AsyncIterator[None]:
    settings = ScoringSettings()
    configure_sentry(settings)
    credential = DefaultAzureCredential()

    http_client = httpx.AsyncClient(
        base_url=settings.model_router_url,
        timeout=settings.model_router_timeout_seconds,
    )
    retry_config = RetryConfig(
        max_retries=settings.model_router_max_retries,
        backoff_max_seconds=settings.model_router_backoff_max_seconds,
        honor_retry_after=settings.model_router_honor_retry_after,
        total_retry_budget_seconds=settings.model_router_total_retry_budget_seconds,
    )
    model_router_client = ModelRouterClient(
        client=http_client,
        retry_config=retry_config,
    )

    cosmos_client = get_cosmos_client(settings, credential)
    db = cosmos_client.get_database_client(settings.cosmos_database)
    verdicts_container = db.get_container_client(settings.verdicts_container)
    candidates_container = db.get_container_client(settings.candidates_container)
    evidence_container = db.get_container_client(settings.evidence_container)
    seeding_container = db.get_container_client(settings.seeding_container)
    batches_container = db.get_container_client(settings.batches_container)

    sb_client = get_servicebus_client(settings, credential)
    verdict_repository = CosmosVerdictRepository(verdicts_container)
    candidate_repository = CosmosCandidateRepository(candidates_container)
    evidence_repository = CosmosEvidenceBundleRepository(evidence_container)
    seeding_repository = CosmosClaimSeedRepository(seeding_container)
    publisher = ServiceBusEventPublisher(sb_client)

    app.state.store_verdict_deps = StoreVerdictDeps(
        repository=verdict_repository,
        publisher=publisher,
        settings=settings,
    )

    score_handler = _build_score_handler(
        settings, verdict_repository, publisher, model_router_client
    )
    app.state.score_verdict_deps = ScoreVerdictDeps(
        repository=verdict_repository,
        publisher=publisher,
        settings=settings,
        model_router_client=model_router_client,
    )
    app.state.get_verdict_deps = GetVerdictHandlerDeps(
        verdict_repo=verdict_repository,
        candidate_repo=candidate_repository,
        evidence_repo=evidence_repository,
        seeding_repo=seeding_repository,
    )

    claim_draft_generator = None
    if settings.claim_draft_enabled:
        claim_draft_generator = ClaimDraftGenerator(
            ClaimDraftGeneratorOptions(
                client=http_client,
                timeout_seconds=settings.claim_draft_timeout_seconds,
            )
        )

    process_handler = _build_process_handler(
        settings,
        verdict_repository,
        candidate_repository,
        evidence_repository,
        score_handler,
        publisher,
        claim_draft_generator,
    )
    batch_gate = TerminalBatchGate(
        container=batches_container,
        cache_ttl_seconds=settings.batch_terminal_cache_ttl_seconds,
    )
    consumer = ServiceBusConsumer(
        client=sb_client,
        handler=process_handler,
        topic=settings.scoring_requested_topic,
        subscription=settings.scoring_subscription,
        max_attempts=settings.consumer_max_attempts,
        publisher=publisher,
        failed_topic=settings.scoring_failed_topic,
        session_idle_timeout=settings.consumer_session_idle_timeout,
        session_lock_renewal_seconds=settings.consumer_session_lock_renewal_seconds,
        max_concurrent_sessions=settings.consumer_max_concurrent_sessions,
        batch_gate=batch_gate,
    )

    health = ConsumerHealth()
    app.state.consumer_health = health

    cancel_event = asyncio.Event()
    consumer_task = asyncio.create_task(consumer.run(cancel_event))
    health.mark_alive()
    supervise(consumer_task, health, _logger)

    _logger.info(
        "Scoring service started (dual_mode_enabled=%s) — single-mode runs are expected to "
        "report agreement_level=single_configured; agreement_level=fallback_single indicates a "
        "real dual-mode degrade (secondary unavailable/timeout/parse failure)",
        settings.dual_mode_enabled,
    )
    yield

    cancel_event.set()
    consumer_task.cancel()
    try:
        await consumer_task
    except asyncio.CancelledError:
        pass

    await cosmos_client.close()
    await sb_client.close()
    await credential.close()
    await http_client.aclose()
    _logger.info("Scoring service stopped")


def create_app() -> FastAPI:
    app = FastAPI(title="Scoring Service", lifespan=_lifespan)
    app.include_router(scoring_router)

    @app.get("/health", response_model=None)
    async def health() -> dict | Response:
        consumer_health = getattr(app.state, "consumer_health", None)
        if consumer_health is None or consumer_health.is_serving:
            return {"status": "ok"}
        return JSONResponse(status_code=503, content={"status": "consumer_dead"})

    return app


app = create_app()
