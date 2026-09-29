from unittest.mock import AsyncMock

from seeding.application.handlers.generate_landscape_handler import (
    GenerateLandscapeDeps,
    GenerateLandscapeHandler,
)
from seeding.application.handlers.process_seeding_request_handler import ProcessOutcome
from seeding.domain.errors.seeding_errors import (
    EvidenceServiceTransientError,
    VectorRouterTransientError,
)
from seeding.domain.events.landscape import LandscapeEnvelope, LandscapePayload
from seeding.domain.models.digest import CitedEntry, InventionContextBrief
from seeding.domain.models.landscape import LandscapeSourceFlags, PriorArtLandscape
from seeding.infrastructure.clients.evidence_client import (
    EvidenceSearchResult,
    LivePatentMatch,
    LiveSourceStatus,
)
from seeding.infrastructure.config.settings import SeedingSettings

BATCH_ID = "batch-1"
DOCUMENT_ID = "doc-1"
CORRELATION_ID = "corr-1"


def _settings(**overrides) -> SeedingSettings:
    base = {"model_router_url": "http://model-router.internal.test"}
    base.update(overrides)
    return SeedingSettings(**base)


def _envelope() -> LandscapeEnvelope:
    return LandscapeEnvelope(
        batch_id=BATCH_ID,
        document_id=DOCUMENT_ID,
        correlation_id=CORRELATION_ID,
        payload=LandscapePayload(document_id=DOCUMENT_ID, ai_model="gpt-x"),
    )


def _brief() -> InventionContextBrief:
    return InventionContextBrief(
        id="brief-1",
        batch_id=BATCH_ID,
        document_id=DOCUMENT_ID,
        contributions=[CitedEntry(text="a novel widget mechanism here", chunk_ids=["c1"])],
        future_work=[],
        key_concepts=[CitedEntry(text="neural cache", chunk_ids=["c0"])],
        tech_fields=[CitedEntry(text="machine learning")],
        schema_version="1.0",
        prompt_version="1.0.0",
    )


def _corpus_hit(hit_id: str, score: float) -> dict:
    return {"id": hit_id, "score": score, "payload": {"chunk_id": hit_id, "document_id": "d"}}


def _live_result(sources: list[LiveSourceStatus], match_count: int = 15) -> EvidenceSearchResult:
    matches = [
        LivePatentMatch(
            reference=f"US{i}",
            title="T",
            applicant="Acme",
            date="2020",
            url="http://x",
            relevance_score=1.0 - i / 100,
            source="USPTO",
        )
        for i in range(match_count)
    ]
    return EvidenceSearchResult(matches=matches, sources=sources)


def _search_by_top_k(concept_hits: list[dict], whitespace_hits: list[dict]):
    async def _search(embedding, top_k, target, filters=None):
        if top_k == 25:
            return concept_hits
        return whitespace_hits

    return _search


def _handler(*, brief=None, existing=None, search=None, evidence=None, settings=None):
    digest_repo = AsyncMock()
    digest_repo.get_brief = AsyncMock(return_value=brief if brief is not None else _brief())
    landscape_repo = AsyncMock()
    landscape_repo.get_landscape = AsyncMock(return_value=existing)
    landscape_repo.save_landscape = AsyncMock()
    vector_client = AsyncMock()
    vector_client.embed = AsyncMock(return_value=[[0.1], [0.2]])
    vector_client.search = search or _search_by_top_k(
        [_corpus_hit("p1", 0.5)], [_corpus_hit("pw", 0.4)]
    )
    evidence_client = AsyncMock()
    evidence_client.search_patents = evidence or AsyncMock(
        return_value=_live_result([LiveSourceStatus("USPTO", "ok", 15, None)])
    )
    publisher = AsyncMock()
    deps = GenerateLandscapeDeps(
        digest_repo=digest_repo,
        landscape_repo=landscape_repo,
        vector_client=vector_client,
        evidence_client=evidence_client,
        publisher=publisher,
        settings=settings or _settings(),
    )
    return GenerateLandscapeHandler(deps), landscape_repo, vector_client, evidence_client, publisher


async def test_happy_path_builds_saves_and_publishes():
    handler, landscape_repo, _, _, publisher = _handler()

    outcome = await handler.handle(_envelope())

    assert outcome == ProcessOutcome.SUCCESS
    saved: PriorArtLandscape = landscape_repo.save_landscape.await_args.args[0]
    assert len(saved.concepts) == 1
    assert saved.concepts[0].live_hit_count == 15
    assert saved.concepts[0].density == "crowded"
    assert saved.source_flags.corpus_only is False
    assert saved.source_flags.live_sources_ok == ["USPTO"]
    assert len(saved.whitespace) == 1
    assert saved.whitespace[0].kind == "contribution"
    event = publisher.publish.await_args.args[1]
    assert event["event_type"] == "landscape.completed"
    assert event["payload"]["landscape_id"] == saved.id
    assert publisher.publish.await_args.kwargs["session_id"] == BATCH_ID


async def test_one_patent_source_degraded_records_flags_and_keeps_whitespace():
    evidence = AsyncMock(
        return_value=_live_result(
            [
                LiveSourceStatus("USPTO", "ok", 15, None),
                LiveSourceStatus("Lens", "api_error", 0, "boom"),
            ]
        )
    )
    handler, landscape_repo, _, _, _ = _handler(evidence=evidence)

    outcome = await handler.handle(_envelope())

    assert outcome == ProcessOutcome.SUCCESS
    saved: PriorArtLandscape = landscape_repo.save_landscape.await_args.args[0]
    assert saved.source_flags.degraded_sources == ["Lens"]
    assert saved.source_flags.live_sources_ok == ["USPTO"]
    assert saved.source_flags.corpus_only is False
    assert len(saved.whitespace) == 1


async def test_evidence_service_down_yields_corpus_only_artifact():
    evidence = AsyncMock(side_effect=EvidenceServiceTransientError("down", status_code=503))
    handler, landscape_repo, _, _, _ = _handler(evidence=evidence)

    outcome = await handler.handle(_envelope())

    assert outcome == ProcessOutcome.SUCCESS
    saved: PriorArtLandscape = landscape_repo.save_landscape.await_args.args[0]
    assert saved.source_flags.corpus_only is True
    assert saved.source_flags.evidence_reachable is False
    assert saved.concepts[0].live_hit_count == 0
    assert saved.concepts[0].live_matches == []


async def test_vector_router_down_is_transient_without_save():
    search = AsyncMock(side_effect=VectorRouterTransientError("down", status_code=503))
    handler, landscape_repo, _, _, publisher = _handler(search=search)

    outcome = await handler.handle(_envelope())

    assert outcome == ProcessOutcome.TRANSIENT
    landscape_repo.save_landscape.assert_not_awaited()
    publisher.publish.assert_not_awaited()


async def test_time_budget_exceeded_returns_transient():
    handler, landscape_repo, _, _, publisher = _handler(
        settings=_settings(landscape_message_time_budget_seconds=-1.0)
    )

    outcome = await handler.handle(_envelope())

    assert outcome == ProcessOutcome.TRANSIENT
    landscape_repo.save_landscape.assert_not_awaited()
    publisher.publish.assert_not_awaited()


async def test_missing_brief_returns_transient():
    digest_repo = AsyncMock()
    digest_repo.get_brief = AsyncMock(return_value=None)
    landscape_repo = AsyncMock()
    vector_client = AsyncMock()
    evidence_client = AsyncMock()
    publisher = AsyncMock()
    deps = GenerateLandscapeDeps(
        digest_repo=digest_repo,
        landscape_repo=landscape_repo,
        vector_client=vector_client,
        evidence_client=evidence_client,
        publisher=publisher,
        settings=_settings(),
    )
    handler = GenerateLandscapeHandler(deps)

    outcome = await handler.handle(_envelope())

    assert outcome == ProcessOutcome.TRANSIENT
    landscape_repo.get_landscape.assert_not_awaited()
    publisher.publish.assert_not_awaited()


async def test_idempotent_replay_republishes_without_regenerating():
    existing = PriorArtLandscape(
        id="landscape-1",
        batch_id=BATCH_ID,
        document_id=DOCUMENT_ID,
        schema_version="1.0",
        source_flags=LandscapeSourceFlags(evidence_reachable=True, corpus_only=False),
    )
    handler, landscape_repo, vector_client, evidence_client, publisher = _handler(existing=existing)

    outcome = await handler.handle(_envelope())

    assert outcome == ProcessOutcome.SUCCESS
    landscape_repo.save_landscape.assert_not_awaited()
    vector_client.embed.assert_not_awaited()
    evidence_client.search_patents.assert_not_awaited()
    assert publisher.publish.await_args.args[1]["payload"]["landscape_id"] == "landscape-1"
