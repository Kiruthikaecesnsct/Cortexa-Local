from unittest.mock import AsyncMock, patch

from seeding.application.handlers.process_seeding_request_handler import (
    ProcessOutcome,
    ProcessSeedingRequestDeps,
    ProcessSeedingRequestHandler,
)
from seeding.application.ideation.deep_seeding_generation import DeepGenerationOutcome
from seeding.domain.errors.seeding_errors import SeedingReportNotFoundError
from seeding.domain.events.event_envelope import EventEnvelope, EventPayload
from seeding.domain.models.scratchpad import AcceptedIdea
from seeding.domain.models.seeding_result import SeedingResult
from seeding.infrastructure.config.settings import SeedingSettings

BATCH_ID = "batch-deep"
CORRELATION_ID = "corr-deep"
DOC_ID = "doc-deep"

SETTINGS = SeedingSettings(model_router_url="http://model-router.internal.test")

_DEEP_GENERATE_PATH = (
    "seeding.application.handlers.process_seeding_request_handler.generate_deep_seeding"
)


def _envelope() -> EventEnvelope:
    return EventEnvelope(
        batch_id=BATCH_ID,
        correlation_id=CORRELATION_ID,
        document_id=DOC_ID,
        payload=EventPayload(seeding_mode="deep"),
    )


def _accepted_idea(title: str, round_index: int) -> AcceptedIdea:
    return AcceptedIdea(
        title=title,
        summary="a problem",
        mechanism="a mechanism",
        claim_statement="A method comprising steps.",
        category="Whitespace",
        round_index=round_index,
        chunk_ids=["c1"],
    )


def _make_handler():
    report_repo = AsyncMock()
    report_repo.get_by_batch.side_effect = SeedingReportNotFoundError(BATCH_ID)
    candidate_write_repo = AsyncMock()
    publisher = AsyncMock()
    landscape_repo = AsyncMock()
    landscape_repo.get_landscape.return_value = None

    deps = ProcessSeedingRequestDeps(
        candidate_repo=AsyncMock(),
        report_repo=report_repo,
        landscape_repo=landscape_repo,
        publisher=publisher,
        client=AsyncMock(),
        settings=SETTINGS,
        candidate_write_repo=candidate_write_repo,
    )
    return ProcessSeedingRequestHandler(deps), report_repo, candidate_write_repo, publisher


async def test_deep_with_accepted_ideas_persists_candidates_and_publishes_ideation_completed():
    handler, report_repo, candidate_write_repo, publisher = _make_handler()
    ideas = [_accepted_idea("Idea A", 0), _accepted_idea("Idea B", 1)]
    outcome_payload = DeepGenerationOutcome(
        result=SeedingResult(id="r", batch_id=BATCH_ID, opportunities=[]),
        resume_needed=False,
        accepted_ideas=ideas,
    )

    with patch(_DEEP_GENERATE_PATH, new_callable=AsyncMock, return_value=outcome_payload):
        outcome = await handler.handle(_envelope())

    assert outcome == ProcessOutcome.SUCCESS
    candidate_write_repo.upsert_many.assert_awaited_once()
    persisted = candidate_write_repo.upsert_many.await_args.args[0]
    assert len(persisted) == 2
    assert all(doc["engine"] == "seeding" for doc in persisted)

    report_repo.save.assert_not_awaited()
    publisher.publish.assert_awaited_once()
    topic, event = publisher.publish.await_args.args
    assert topic == SETTINGS.ideation_completed_topic
    assert event["event_type"] == "ideation.completed"
    assert event["payload"]["candidate_count"] == 2
    assert event["payload"]["candidate_ids"] == [doc["id"] for doc in persisted]
    assert event["payload"]["job_id"] == BATCH_ID


async def test_deep_with_accepted_ideas_publishes_with_session_and_correlation():
    handler, _, _, publisher = _make_handler()
    outcome_payload = DeepGenerationOutcome(
        result=SeedingResult(id="r", batch_id=BATCH_ID, opportunities=[]),
        resume_needed=False,
        accepted_ideas=[_accepted_idea("Idea A", 0)],
    )

    with patch(_DEEP_GENERATE_PATH, new_callable=AsyncMock, return_value=outcome_payload):
        await handler.handle(_envelope())

    _, kwargs = publisher.publish.call_args
    assert kwargs.get("session_id") == BATCH_ID
    assert kwargs.get("correlation_id") == CORRELATION_ID


async def test_deep_zero_accepted_ideas_saves_report_and_publishes_engine_completed():
    handler, report_repo, candidate_write_repo, publisher = _make_handler()
    empty_result = SeedingResult(
        id="report-empty",
        batch_id=BATCH_ID,
        document_id=DOC_ID,
        opportunities=[],
        is_empty=True,
    )
    outcome_payload = DeepGenerationOutcome(
        result=empty_result, resume_needed=False, accepted_ideas=[]
    )

    with patch(_DEEP_GENERATE_PATH, new_callable=AsyncMock, return_value=outcome_payload):
        outcome = await handler.handle(_envelope())

    assert outcome == ProcessOutcome.SUCCESS
    candidate_write_repo.upsert_many.assert_not_awaited()
    report_repo.save.assert_awaited_once_with(empty_result)
    topic, event = publisher.publish.await_args.args
    assert topic == SETTINGS.engine_completed_topic
    assert event["event_type"] == "engine.completed"


async def test_deep_with_accepted_ideas_id_is_stable_across_redelivery():
    handler, _, candidate_write_repo, _ = _make_handler()
    ideas = [_accepted_idea("Idea A", 0)]
    outcome_payload = DeepGenerationOutcome(
        result=SeedingResult(id="r", batch_id=BATCH_ID, opportunities=[]),
        resume_needed=False,
        accepted_ideas=ideas,
    )

    with patch(_DEEP_GENERATE_PATH, new_callable=AsyncMock, return_value=outcome_payload):
        await handler.handle(_envelope())
        first = candidate_write_repo.upsert_many.await_args.args[0][0]["id"]
        await handler.handle(_envelope())
        second = candidate_write_repo.upsert_many.await_args.args[0][0]["id"]

    assert first == second
