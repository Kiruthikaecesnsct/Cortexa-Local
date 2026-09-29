from unittest.mock import AsyncMock

from seeding.application.handlers.process_seeding_report_request_handler import (
    ProcessSeedingReportRequestDeps,
    ProcessSeedingReportRequestHandler,
)
from seeding.application.handlers.process_seeding_request_handler import ProcessOutcome
from seeding.domain.errors.seeding_errors import SeedingReportNotFoundError, StorageWriteError
from seeding.domain.events.event_envelope import EventEnvelope, EventPayload
from seeding.domain.models.landscape import ConceptLandscape, LiveMatch, PriorArtLandscape
from seeding.domain.models.seeding_result import SeedingResult
from seeding.infrastructure.config.settings import SeedingSettings

BATCH_ID = "batch-report"
CORRELATION_ID = "corr-report"
DOC_ID = "doc-report"

SETTINGS = SeedingSettings(model_router_url="http://model-router.internal.test")


def _envelope() -> EventEnvelope:
    return EventEnvelope(
        batch_id=BATCH_ID,
        correlation_id=CORRELATION_ID,
        document_id=DOC_ID,
        payload=EventPayload(report_trigger="validation"),
    )


def _candidate(candidate_id: str) -> dict:
    return {
        "id": candidate_id,
        "batch_id": BATCH_ID,
        "document_id": DOC_ID,
        "engine": "seeding",
        "claim_text": "A method comprising X.",
        "problem": "problem",
        "mechanism": "mechanism",
        "title": "Title",
        "description": "Description",
        "category": "Whitespace",
        "provenance": {"chunk_ids": [], "excerpts": []},
    }


def _verdict(candidate_id: str) -> dict:
    return {
        "candidate_id": candidate_id,
        "batch_id": BATCH_ID,
        "composite_score": 50.0,
        "axes": {"Novelty": {"score": 60, "refs": []}},
    }


def _make_handler(*, report_found: bool = False):
    candidate_repo = AsyncMock()
    verdict_repo = AsyncMock()
    evidence_repo = AsyncMock()
    report_repo = AsyncMock()
    landscape_repo = AsyncMock()
    landscape_repo.get_landscape.return_value = None
    publisher = AsyncMock()

    candidate_repo.get_seeded_by_batch.return_value = [_candidate("seed-1"), _candidate("seed-2")]
    verdict_repo.get_for_candidates.return_value = [_verdict("seed-1")]
    evidence_repo.get_for_candidates.return_value = {}

    if report_found:
        report_repo.get_by_batch.return_value = SeedingResult(
            id="existing", batch_id=BATCH_ID, document_id=DOC_ID, opportunities=[]
        )
    else:
        report_repo.get_by_batch.side_effect = SeedingReportNotFoundError(BATCH_ID)

    deps = ProcessSeedingReportRequestDeps(
        candidate_repo=candidate_repo,
        verdict_repo=verdict_repo,
        evidence_repo=evidence_repo,
        report_repo=report_repo,
        landscape_repo=landscape_repo,
        publisher=publisher,
        settings=SETTINGS,
    )
    return ProcessSeedingReportRequestHandler(deps), report_repo, publisher, verdict_repo


async def test_builds_report_from_seeded_candidates_and_verdicts():
    handler, report_repo, publisher, verdict_repo = _make_handler()

    outcome = await handler.handle(_envelope())

    assert outcome == ProcessOutcome.SUCCESS
    verdict_repo.get_for_candidates.assert_awaited_once()
    _, candidate_ids = verdict_repo.get_for_candidates.await_args.args
    assert candidate_ids == {"seed-1", "seed-2"}

    saved = report_repo.save.await_args.args[0]
    assert saved.engine == "seeding"
    assert [opp.candidate_id for opp in saved.opportunities] == ["seed-1"]
    assert saved.is_empty is False

    topic, event = publisher.publish.await_args.args
    assert topic == SETTINGS.engine_completed_topic
    assert event["event_type"] == "engine.completed"


async def test_report_is_empty_when_no_verdicts():
    handler, report_repo, _, verdict_repo = _make_handler()
    verdict_repo.get_for_candidates.return_value = []

    await handler.handle(_envelope())

    saved = report_repo.save.await_args.args[0]
    assert saved.opportunities == []
    assert saved.is_empty is True


async def test_idempotent_when_report_already_exists():
    handler, report_repo, publisher, verdict_repo = _make_handler(report_found=True)

    outcome = await handler.handle(_envelope())

    assert outcome == ProcessOutcome.SUCCESS
    report_repo.save.assert_not_awaited()
    verdict_repo.get_for_candidates.assert_not_awaited()
    publisher.publish.assert_awaited_once()


async def test_storage_error_returns_transient():
    handler, report_repo, _, _ = _make_handler()
    report_repo.save.side_effect = StorageWriteError("cosmos down")

    outcome = await handler.handle(_envelope())

    assert outcome == ProcessOutcome.TRANSIENT


async def test_deterministic_report_id_for_upsert_idempotency():
    handler, report_repo, _, _ = _make_handler()

    await handler.handle(_envelope())

    saved = report_repo.save.await_args.args[0]
    assert saved.id == f"seeding-report-{BATCH_ID}"


def _landscape() -> PriorArtLandscape:
    return PriorArtLandscape(
        id="landscape-report",
        batch_id=BATCH_ID,
        document_id=DOC_ID,
        schema_version="1.0",
        concepts=[
            ConceptLandscape(
                concept="Memory",
                chunk_ids=["c1"],
                density="dense",
                live_matches=[
                    LiveMatch(
                        reference="US-9",
                        title="Prior art nine",
                        url="http://p/9",
                        source="USPTO",
                        relevance_score=0.9,
                    )
                ],
            )
        ],
    )


async def test_landscape_populates_proximity_and_concept_map():
    handler, report_repo, _, _ = _make_handler()
    candidate = _candidate("seed-1")
    candidate["provenance"] = {"chunk_ids": ["c1"], "excerpts": []}
    handler._deps.candidate_repo.get_seeded_by_batch.return_value = [candidate]
    handler._deps.landscape_repo.get_landscape.return_value = _landscape()

    await handler.handle(_envelope())

    saved = report_repo.save.await_args.args[0]
    assert saved.opportunities[0].prior_art_proximity[0].reference == "US-9"
    assert saved.concept_map[0].concept == "Memory"
    assert saved.concept_map[0].opportunity_ids == ["seed-1"]
    assert saved.landscape.landscape_id == "landscape-report"


async def test_landscape_read_failure_degrades_to_no_proximity():
    handler, report_repo, _, _ = _make_handler()
    candidate = _candidate("seed-1")
    candidate["provenance"] = {"chunk_ids": ["c1"], "excerpts": []}
    handler._deps.candidate_repo.get_seeded_by_batch.return_value = [candidate]
    handler._deps.landscape_repo.get_landscape.side_effect = StorageWriteError("cosmos down")

    outcome = await handler.handle(_envelope())

    assert outcome == ProcessOutcome.SUCCESS
    saved = report_repo.save.await_args.args[0]
    assert saved.opportunities[0].prior_art_proximity == []
    assert saved.concept_map == []
    assert saved.landscape is None
