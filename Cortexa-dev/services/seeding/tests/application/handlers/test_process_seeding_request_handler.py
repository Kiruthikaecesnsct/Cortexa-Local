import logging
from unittest.mock import AsyncMock, patch

from seeding.application.handlers.process_seeding_request_handler import (
    ProcessOutcome,
    ProcessSeedingRequestDeps,
    ProcessSeedingRequestHandler,
)
from seeding.application.ideation.deep_seeding_generation import DeepGenerationOutcome
from seeding.domain.errors.seeding_errors import (
    EventPublishError,
    ModelRouterFailedError,
    OpportunityParseError,
    SeedingReportNotFoundError,
    StorageWriteError,
    UngroundedSeedingError,
)
from seeding.domain.events.event_envelope import EventEnvelope, EventPayload
from seeding.domain.models.landscape import LandscapeSourceFlags, PriorArtLandscape
from seeding.domain.models.seeding_result import SeedingOpportunity, SeedingResult
from seeding.infrastructure.config.settings import SeedingSettings

BATCH_ID = "batch-001"
CORRELATION_ID = "corr-001"
DOC_ID = "doc-001"
REPORT_ID = "report-001"
ROADMAP = "secret-roadmap-context-value"

SETTINGS = SeedingSettings(model_router_url="http://model-router.internal.test")

_GENERATE_PATH = (
    "seeding.application.handlers.process_seeding_request_handler.generate_seeding_opportunities"
)
_DEEP_GENERATE_PATH = (
    "seeding.application.handlers.process_seeding_request_handler.generate_deep_seeding"
)


def _make_envelope(
    roadmap_context: str | None = None, seeding_mode: str | None = None
) -> EventEnvelope:
    return EventEnvelope(
        batch_id=BATCH_ID,
        correlation_id=CORRELATION_ID,
        document_id=DOC_ID,
        payload=EventPayload(roadmap_context=roadmap_context, seeding_mode=seeding_mode),
    )


def _make_report() -> SeedingResult:
    return SeedingResult(
        id=REPORT_ID,
        batch_id=BATCH_ID,
        document_id=DOC_ID,
        engine="seeding",
        opportunities=[],
    )


def _make_handler(*, report_found: bool = False, candidates: list | None = None, landscape=None):
    candidate_repo = AsyncMock()
    report_repo = AsyncMock()
    landscape_repo = AsyncMock()
    landscape_repo.get_landscape.return_value = landscape
    publisher = AsyncMock()
    client = AsyncMock()

    if report_found:
        report_repo.get_by_batch.return_value = _make_report()
    else:
        report_repo.get_by_batch.side_effect = SeedingReportNotFoundError(BATCH_ID)

    candidate_repo.get_by_batch.return_value = candidates or [
        {
            "id": "cand-1",
            "batch_id": BATCH_ID,
            "document_id": DOC_ID,
            "claim_text": "A method for processing data using a novel algorithm.",
            "problem": "Existing systems are slow and inefficient.",
            "mechanism": "Uses parallel processing with optimized data structures.",
            "tech_field": "Computer Science",
            "ipc_cpc_guess": "G06F 16/33",
        }
    ]

    deps = ProcessSeedingRequestDeps(
        candidate_repo=candidate_repo,
        report_repo=report_repo,
        landscape_repo=landscape_repo,
        publisher=publisher,
        client=client,
        settings=SETTINGS,
    )
    return (
        ProcessSeedingRequestHandler(deps),
        candidate_repo,
        report_repo,
        publisher,
        client,
        landscape_repo,
    )


async def test_happy_path_returns_success_and_publishes():
    handler, _, report_repo, publisher, _, _ = _make_handler()
    envelope = _make_envelope()
    generated = _make_report()

    with patch(_GENERATE_PATH, new_callable=AsyncMock, return_value=generated):
        outcome = await handler.handle(envelope)

    assert outcome == ProcessOutcome.SUCCESS
    report_repo.save.assert_awaited_once_with(generated)
    publisher.publish.assert_awaited_once()


def _landscape_artifact() -> PriorArtLandscape:
    return PriorArtLandscape(
        id="landscape-001",
        batch_id=BATCH_ID,
        document_id=DOC_ID,
        schema_version="1.0",
        source_flags=LandscapeSourceFlags(
            evidence_reachable=True, corpus_only=False, degraded_sources=["Lens"]
        ),
    )


async def test_report_includes_landscape_provenance_when_artifact_exists():
    landscape = _landscape_artifact()
    handler, _, report_repo, _, _, landscape_repo = _make_handler(landscape=landscape)
    envelope = _make_envelope()
    generated = _make_report()

    with patch(_GENERATE_PATH, new_callable=AsyncMock, return_value=generated):
        outcome = await handler.handle(envelope)

    assert outcome == ProcessOutcome.SUCCESS
    landscape_repo.get_landscape.assert_awaited_once_with(BATCH_ID, DOC_ID, "1.0")
    saved = report_repo.save.await_args.args[0]
    assert saved.landscape is not None
    assert saved.landscape.landscape_id == "landscape-001"
    assert saved.landscape.schema_version == "1.0"
    assert saved.landscape.source_flags.degraded_sources == ["Lens"]


async def test_report_succeeds_when_landscape_artifact_absent():
    handler, _, report_repo, publisher, _, _ = _make_handler(landscape=None)
    envelope = _make_envelope()
    generated = _make_report()

    with patch(_GENERATE_PATH, new_callable=AsyncMock, return_value=generated):
        outcome = await handler.handle(envelope)

    assert outcome == ProcessOutcome.SUCCESS
    saved = report_repo.save.await_args.args[0]
    assert saved.landscape is None
    assert saved.seeding_mode == "legacy"
    publisher.publish.assert_awaited_once()


async def test_legacy_path_stamps_report_seeding_mode_legacy():
    handler, _, report_repo, _, _, _ = _make_handler()
    envelope = _make_envelope(seeding_mode="legacy")
    generated = _make_report()

    with patch(_GENERATE_PATH, new_callable=AsyncMock, return_value=generated):
        await handler.handle(envelope)

    saved = report_repo.save.await_args.args[0]
    assert saved.seeding_mode == "legacy"


async def test_absent_mode_falls_back_to_settings_default_legacy():
    handler, _, report_repo, _, _, _ = _make_handler()
    envelope = _make_envelope()
    generated = _make_report()

    with patch(_GENERATE_PATH, new_callable=AsyncMock, return_value=generated):
        await handler.handle(envelope)

    saved = report_repo.save.await_args.args[0]
    assert saved.seeding_mode == "legacy"


async def test_idempotent_republish_skips_generation_and_returns_success():
    handler, candidate_repo, report_repo, publisher, _, _ = _make_handler(report_found=True)
    envelope = _make_envelope()

    with patch(_GENERATE_PATH, new_callable=AsyncMock) as mock_gen:
        outcome = await handler.handle(envelope)

    assert outcome == ProcessOutcome.SUCCESS
    mock_gen.assert_not_awaited()
    report_repo.save.assert_not_awaited()
    publisher.publish.assert_awaited_once()
    candidate_repo.get_by_batch.assert_not_awaited()


async def test_idempotent_republish_preserves_correlation_id():
    handler, _, _, publisher, _, _ = _make_handler(report_found=True)
    envelope = _make_envelope()

    with patch(_GENERATE_PATH, new_callable=AsyncMock):
        await handler.handle(envelope)

    _, kwargs = publisher.publish.call_args
    assert kwargs.get("correlation_id") == CORRELATION_ID


async def test_no_candidates_via_ungrounded_error_returns_permanent():
    handler, _, _, publisher, _, _ = _make_handler()
    envelope = _make_envelope()

    with patch(_GENERATE_PATH, side_effect=UngroundedSeedingError("no candidates")):
        outcome = await handler.handle(envelope)

    assert outcome == ProcessOutcome.PERMANENT
    publisher.publish.assert_awaited_once()
    topic, event = publisher.publish.await_args.args
    assert topic == SETTINGS.seeding_failed_topic
    assert event["event_type"] == "seeding.failed"
    assert event["batch_id"] == BATCH_ID
    assert event["document_id"] == DOC_ID
    assert event["payload"]["job_id"] == BATCH_ID
    assert event["payload"]["trigger_type"] == "pipeline"
    assert "no candidates" in event["payload"]["reason"]


async def test_model_router_400_returns_permanent_and_does_not_save():
    handler, _, report_repo, _, _, _ = _make_handler()
    envelope = _make_envelope()

    with patch(_GENERATE_PATH, side_effect=ModelRouterFailedError("bad req", status_code=400)):
        outcome = await handler.handle(envelope)

    assert outcome == ProcessOutcome.PERMANENT
    report_repo.save.assert_not_awaited()


async def test_model_router_503_returns_transient():
    handler, _, _, _, _, _ = _make_handler()
    envelope = _make_envelope()

    with patch(_GENERATE_PATH, side_effect=ModelRouterFailedError("unavailable", status_code=503)):
        outcome = await handler.handle(envelope)

    assert outcome == ProcessOutcome.TRANSIENT


async def test_model_router_network_error_returns_transient():
    handler, _, _, _, _, _ = _make_handler()
    envelope = _make_envelope()

    with patch(_GENERATE_PATH, side_effect=ModelRouterFailedError("timeout", status_code=None)):
        outcome = await handler.handle(envelope)

    assert outcome == ProcessOutcome.TRANSIENT


async def test_model_router_429_returns_transient():
    handler, _, _, _, _, _ = _make_handler()
    envelope = _make_envelope()

    with patch(_GENERATE_PATH, side_effect=ModelRouterFailedError("rate limited", status_code=429)):
        outcome = await handler.handle(envelope)

    assert outcome == ProcessOutcome.TRANSIENT


async def test_opportunity_parse_error_returns_permanent():
    handler, _, _, _, _, _ = _make_handler()
    envelope = _make_envelope()

    with patch(_GENERATE_PATH, side_effect=OpportunityParseError("bad json")):
        outcome = await handler.handle(envelope)

    assert outcome == ProcessOutcome.PERMANENT


async def test_storage_write_error_on_save_returns_transient():
    handler, _, report_repo, _, _, _ = _make_handler()
    envelope = _make_envelope()
    generated = _make_report()

    with patch(_GENERATE_PATH, new_callable=AsyncMock, return_value=generated):
        report_repo.save.side_effect = StorageWriteError("cosmos down")
        outcome = await handler.handle(envelope)

    assert outcome == ProcessOutcome.TRANSIENT


async def test_publish_error_returns_transient():
    handler, _, _, publisher, _, _ = _make_handler()
    envelope = _make_envelope()
    generated = _make_report()

    with patch(_GENERATE_PATH, new_callable=AsyncMock, return_value=generated):
        publisher.publish.side_effect = EventPublishError("sb down")
        outcome = await handler.handle(envelope)

    assert outcome == ProcessOutcome.TRANSIENT


async def test_session_id_set_to_batch_id_on_publish():
    handler, _, _, publisher, _, _ = _make_handler()
    envelope = _make_envelope()
    generated = _make_report()

    with patch(_GENERATE_PATH, new_callable=AsyncMock, return_value=generated):
        await handler.handle(envelope)

    _, kwargs = publisher.publish.call_args
    assert kwargs.get("session_id") == BATCH_ID


async def test_permanent_outcome_publishes_seeding_failed_with_session_and_correlation():
    handler, _, _, publisher, _, _ = _make_handler()
    envelope = _make_envelope()

    with patch(_GENERATE_PATH, side_effect=OpportunityParseError("bad json")):
        outcome = await handler.handle(envelope)

    assert outcome == ProcessOutcome.PERMANENT
    _, kwargs = publisher.publish.call_args
    assert kwargs.get("session_id") == BATCH_ID
    assert kwargs.get("correlation_id") == CORRELATION_ID


async def test_roadmap_context_not_leaked_to_logs(caplog):
    handler, _, _, _, _, _ = _make_handler()
    envelope = _make_envelope(roadmap_context=ROADMAP)
    generated = _make_report()

    with caplog.at_level(logging.DEBUG):
        with patch(_GENERATE_PATH, new_callable=AsyncMock, return_value=generated):
            await handler.handle(envelope)

    assert ROADMAP not in caplog.text


def _make_deep_report(
    *, opportunities: list | None = None, is_empty: bool = False, explanation: str = ""
) -> SeedingResult:
    return SeedingResult(
        id=REPORT_ID,
        batch_id=BATCH_ID,
        document_id=DOC_ID,
        engine="seeding",
        opportunities=opportunities or [],
        is_empty=is_empty,
        explanation=explanation,
        scratchpad_id="scratchpad-001",
    )


async def test_seeding_mode_absent_invokes_legacy_path_not_deep_engine():
    handler, _, report_repo, publisher, _, _ = _make_handler()
    envelope = _make_envelope()
    generated = _make_report()

    with (
        patch(_GENERATE_PATH, new_callable=AsyncMock, return_value=generated) as mock_legacy,
        patch(_DEEP_GENERATE_PATH, new_callable=AsyncMock) as mock_deep,
    ):
        outcome = await handler.handle(envelope)

    assert outcome == ProcessOutcome.SUCCESS
    mock_legacy.assert_awaited_once()
    mock_deep.assert_not_awaited()
    report_repo.save.assert_awaited_once_with(generated)


async def test_seeding_mode_legacy_string_invokes_legacy_path_not_deep_engine():
    handler, _, _, _, _, _ = _make_handler()
    envelope = _make_envelope(seeding_mode="legacy")
    generated = _make_report()

    with (
        patch(_GENERATE_PATH, new_callable=AsyncMock, return_value=generated) as mock_legacy,
        patch(_DEEP_GENERATE_PATH, new_callable=AsyncMock) as mock_deep,
    ):
        outcome = await handler.handle(envelope)

    assert outcome == ProcessOutcome.SUCCESS
    mock_legacy.assert_awaited_once()
    mock_deep.assert_not_awaited()


async def test_seeding_mode_deep_invokes_round_engine_not_legacy_path():
    handler, _, report_repo, publisher, _, _ = _make_handler()
    envelope = _make_envelope(seeding_mode="deep")
    generated = _make_deep_report(
        opportunities=[
            SeedingOpportunity(
                id="opp-1",
                title="A grounded whitespace opportunity",
                description="description",
                confidence_score=0.0,
                roadmap_alignment="",
            )
        ]
    )
    outcome_payload = DeepGenerationOutcome(result=generated, resume_needed=False)

    with (
        patch(_GENERATE_PATH, new_callable=AsyncMock) as mock_legacy,
        patch(
            _DEEP_GENERATE_PATH, new_callable=AsyncMock, return_value=outcome_payload
        ) as mock_deep,
    ):
        outcome = await handler.handle(envelope)

    assert outcome == ProcessOutcome.SUCCESS
    mock_deep.assert_awaited_once()
    mock_legacy.assert_not_awaited()
    report_repo.save.assert_awaited_once_with(generated)
    publisher.publish.assert_awaited_once()


async def test_deep_zero_idea_exhausted_returns_success_with_empty_report_and_explanation():
    handler, _, report_repo, publisher, _, _ = _make_handler()
    envelope = _make_envelope(seeding_mode="deep")
    explanation = "ideation produced no novel additions beyond the document over 6 rounds"
    generated = _make_deep_report(opportunities=[], is_empty=True, explanation=explanation)
    outcome_payload = DeepGenerationOutcome(result=generated, resume_needed=False)

    with patch(_DEEP_GENERATE_PATH, new_callable=AsyncMock, return_value=outcome_payload):
        outcome = await handler.handle(envelope)

    assert outcome == ProcessOutcome.SUCCESS
    saved = report_repo.save.await_args.args[0]
    assert saved.is_empty is True
    assert saved.opportunities == []
    assert saved.explanation == explanation
    assert saved.seeding_mode == "deep"
    publisher.publish.assert_awaited_once()
    topic, event = publisher.publish.await_args.args
    assert topic == SETTINGS.engine_completed_topic
    assert event["event_type"] == "engine.completed"


async def test_deep_resume_needed_returns_transient_no_save_no_publish():
    handler, _, report_repo, publisher, _, _ = _make_handler()
    envelope = _make_envelope(seeding_mode="deep")
    outcome_payload = DeepGenerationOutcome(result=None, resume_needed=True)

    with patch(_DEEP_GENERATE_PATH, new_callable=AsyncMock, return_value=outcome_payload):
        outcome = await handler.handle(envelope)

    assert outcome == ProcessOutcome.TRANSIENT
    report_repo.save.assert_not_awaited()
    publisher.publish.assert_not_awaited()


async def test_deep_roadmap_alignment_empty_when_not_supplied_not_hardcoded():
    handler, _, report_repo, _, _, _ = _make_handler()
    envelope = _make_envelope(seeding_mode="deep", roadmap_context=None)
    generated = _make_deep_report(
        opportunities=[
            SeedingOpportunity(
                id="opp-1",
                title="title",
                description="description",
                confidence_score=0.0,
                roadmap_alignment="",
            )
        ]
    )
    outcome_payload = DeepGenerationOutcome(result=generated, resume_needed=False)

    with patch(_DEEP_GENERATE_PATH, new_callable=AsyncMock, return_value=outcome_payload):
        await handler.handle(envelope)

    saved = report_repo.save.await_args.args[0]
    assert saved.opportunities[0].roadmap_alignment == ""


async def test_deep_roadmap_alignment_preserved_when_supplied():
    handler, _, report_repo, _, _, _ = _make_handler()
    envelope = _make_envelope(seeding_mode="deep", roadmap_context="expand into edge devices")
    generated = _make_deep_report(
        opportunities=[
            SeedingOpportunity(
                id="opp-1",
                title="title",
                description="description",
                confidence_score=0.0,
                roadmap_alignment="aligns with edge-device roadmap track",
            )
        ]
    )
    outcome_payload = DeepGenerationOutcome(result=generated, resume_needed=False)

    with patch(_DEEP_GENERATE_PATH, new_callable=AsyncMock, return_value=outcome_payload):
        await handler.handle(envelope)

    saved = report_repo.save.await_args.args[0]
    assert saved.opportunities[0].roadmap_alignment == "aligns with edge-device roadmap track"
