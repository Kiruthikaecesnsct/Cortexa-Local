from unittest.mock import AsyncMock

from harvesting.application.dtos.assemble_response import AssembleResponseDto
from harvesting.application.handlers.process_harvesting_request_handler import (
    ProcessHarvestingRequestDeps,
    ProcessHarvestingRequestHandler,
    ProcessOutcome,
)
from harvesting.domain.errors.harvesting_errors import (
    AxisMissingError,
    EventPublishError,
    ReportNotFoundError,
    StoragePermanentError,
    StorageWriteError,
)
from harvesting.domain.events.event_envelope import EventEnvelope
from harvesting.domain.models.harvesting_report import HarvestingReport
from harvesting.domain.models.rank_weights import RankWeights
from harvesting.infrastructure.config.settings import HarvestingSettings

BATCH_ID = "batch-001"
CORRELATION_ID = "corr-001"
CANDIDATE_ID = "cand-001"
DOC_ID = "doc-001"
REPORT_ID = "report-001"
AXIS_SCORE = 80

WEIGHTS = RankWeights(novelty=0.4, feasibility=0.25, strategic=0.2, patentability=0.15)
SETTINGS = HarvestingSettings(
    cosmos_uri="https://test.example.com",
    servicebus_namespace_fqdn="test.servicebus.windows.net",
)


def _make_envelope() -> EventEnvelope:
    return EventEnvelope(
        batch_id=BATCH_ID,
        correlation_id=CORRELATION_ID,
        payload={},
    )


def _make_candidate_dict(candidate_id: str = CANDIDATE_ID) -> dict:
    return {
        "candidate_id": candidate_id,
        "batch_id": BATCH_ID,
        "document_id": DOC_ID,
        "title": "Test Invention",
        "description": "A novel method for testing.",
    }


def _make_verdict_dict(candidate_id: str = CANDIDATE_ID) -> dict:
    return {
        "candidate_id": candidate_id,
        "batch_id": BATCH_ID,
        "axes": {
            "Novelty": {"score": AXIS_SCORE, "refs": []},
            "Inventiveness": {"score": AXIS_SCORE, "refs": []},
            "Strategic": {"score": AXIS_SCORE, "refs": []},
            "Patentability": {"score": AXIS_SCORE, "refs": []},
        },
    }


def _make_report() -> HarvestingReport:
    return HarvestingReport(
        id=REPORT_ID,
        batch_id=BATCH_ID,
        document_id=DOC_ID,
        candidates=[],
    )


def _make_handler():
    candidate_repo = AsyncMock()
    verdict_repo = AsyncMock()
    evidence_repo = AsyncMock()
    report_repo = AsyncMock()
    publisher = AsyncMock()
    failed_publisher = AsyncMock()
    assemble_handler_mock = AsyncMock()

    report_repo.get_by_batch.side_effect = ReportNotFoundError(BATCH_ID)
    candidate_repo.get_by_batch.return_value = []
    verdict_repo.get_by_batch.return_value = []
    evidence_repo.get_by_batch.return_value = []
    assemble_handler_mock.handle.return_value = AssembleResponseDto(
        report_id=REPORT_ID, candidate_count=1
    )

    deps = ProcessHarvestingRequestDeps(
        candidate_repo=candidate_repo,
        verdict_repo=verdict_repo,
        evidence_repo=evidence_repo,
        report_repo=report_repo,
        publisher=publisher,
        failed_publisher=failed_publisher,
        assemble_handler=assemble_handler_mock,
        weights=WEIGHTS,
        settings=SETTINGS,
    )
    return (
        ProcessHarvestingRequestHandler(deps),
        candidate_repo,
        verdict_repo,
        report_repo,
        publisher,
        assemble_handler_mock,
        failed_publisher,
    )


async def test_handle_new_batch_with_valid_candidates_returns_success():
    handler, candidate_repo, verdict_repo, _, _, _, _ = _make_handler()
    candidate_repo.get_by_batch.return_value = [_make_candidate_dict()]
    verdict_repo.get_by_batch.return_value = [_make_verdict_dict()]
    envelope = _make_envelope()

    outcome = await handler.handle(envelope)

    assert outcome == ProcessOutcome.SUCCESS


async def test_handle_existing_report_skips_candidate_repo_and_returns_success():
    handler, candidate_repo, _, report_repo, publisher, _, _ = _make_handler()
    report_repo.get_by_batch.side_effect = None
    report_repo.get_by_batch.return_value = _make_report()
    envelope = _make_envelope()

    outcome = await handler.handle(envelope)

    assert outcome == ProcessOutcome.SUCCESS
    publisher.publish.assert_awaited_once()
    candidate_repo.get_by_batch.assert_not_awaited()


async def test_handle_existing_report_publishes_with_matching_correlation_id():
    handler, _, _, report_repo, publisher, _, _ = _make_handler()
    report_repo.get_by_batch.side_effect = None
    report_repo.get_by_batch.return_value = _make_report()
    envelope = _make_envelope()

    await handler.handle(envelope)

    published_event = publisher.publish.call_args.args[0]
    assert published_event.correlation_id == CORRELATION_ID


async def test_handle_existing_report_publishes_with_engine_harvesting():
    handler, _, _, report_repo, publisher, _, _ = _make_handler()
    report_repo.get_by_batch.side_effect = None
    report_repo.get_by_batch.return_value = _make_report()
    envelope = _make_envelope()

    await handler.handle(envelope)

    published_event = publisher.publish.call_args.args[0]
    assert published_event.payload["engine"] == "harvesting"


async def test_handle_empty_candidates_returns_permanent():
    handler, _, _, _, _, _, _ = _make_handler()
    envelope = _make_envelope()

    outcome = await handler.handle(envelope)

    assert outcome == ProcessOutcome.PERMANENT


async def test_handle_empty_candidates_publishes_harvesting_failed():
    handler, _, _, _, _, _, failed_publisher = _make_handler()
    envelope = _make_envelope()

    outcome = await handler.handle(envelope)

    assert outcome == ProcessOutcome.PERMANENT
    failed_publisher.publish.assert_awaited_once()
    topic, event = failed_publisher.publish.await_args.args
    assert topic == SETTINGS.harvesting_failed_topic
    assert event.event_type == "harvesting.failed"
    assert event.batch_id == BATCH_ID
    assert event.payload["reason"] == "no_candidates"
    assert event.payload["job_id"] == BATCH_ID
    assert event.payload["trigger_type"] == "pipeline"


async def test_handle_no_assembled_candidates_publishes_harvesting_failed():
    handler, candidate_repo, verdict_repo, _, _, _, failed_publisher = _make_handler()
    candidate_repo.get_by_batch.return_value = [_make_candidate_dict()]
    verdict_repo.get_by_batch.return_value = []
    envelope = _make_envelope()

    outcome = await handler.handle(envelope)

    assert outcome == ProcessOutcome.PERMANENT
    failed_publisher.publish.assert_awaited_once()
    _, event = failed_publisher.publish.await_args.args
    assert event.payload["reason"] == "no_assembled_candidates"


async def test_handle_storage_write_error_from_assemble_handler_returns_transient():
    handler, candidate_repo, verdict_repo, _, _, assemble_handler_mock, failed_publisher = (
        _make_handler()
    )
    candidate_repo.get_by_batch.return_value = [_make_candidate_dict()]
    verdict_repo.get_by_batch.return_value = [_make_verdict_dict()]
    assemble_handler_mock.handle.side_effect = StorageWriteError("cosmos down")
    envelope = _make_envelope()

    outcome = await handler.handle(envelope)

    assert outcome == ProcessOutcome.TRANSIENT
    failed_publisher.publish.assert_not_awaited()


async def test_handle_event_publish_error_on_idempotent_republish_returns_transient():
    handler, _, _, report_repo, publisher, _, _ = _make_handler()
    report_repo.get_by_batch.side_effect = None
    report_repo.get_by_batch.return_value = _make_report()
    publisher.publish.side_effect = EventPublishError("service bus down")
    envelope = _make_envelope()

    outcome = await handler.handle(envelope)

    assert outcome == ProcessOutcome.TRANSIENT


async def test_handle_axis_missing_error_from_assemble_handler_returns_permanent():
    handler, candidate_repo, verdict_repo, _, _, assemble_handler_mock, _ = _make_handler()
    candidate_repo.get_by_batch.return_value = [_make_candidate_dict()]
    verdict_repo.get_by_batch.return_value = [_make_verdict_dict()]
    assemble_handler_mock.handle.side_effect = AxisMissingError("Novelty")
    envelope = _make_envelope()

    outcome = await handler.handle(envelope)

    assert outcome == ProcessOutcome.PERMANENT


async def test_handle_axis_missing_error_publishes_harvesting_failed():
    handler, candidate_repo, verdict_repo, _, _, assemble_handler_mock, failed_publisher = (
        _make_handler()
    )
    candidate_repo.get_by_batch.return_value = [_make_candidate_dict()]
    verdict_repo.get_by_batch.return_value = [_make_verdict_dict()]
    assemble_handler_mock.handle.side_effect = AxisMissingError("Novelty")
    envelope = _make_envelope()

    outcome = await handler.handle(envelope)

    assert outcome == ProcessOutcome.PERMANENT
    failed_publisher.publish.assert_awaited_once()
    topic, event = failed_publisher.publish.await_args.args
    assert topic == SETTINGS.harvesting_failed_topic
    assert event.event_type == "harvesting.failed"
    assert event.payload["trigger_type"] == "pipeline"


async def test_handle_evidence_bundle_read_failure_degrades_to_empty_citations():
    """A persistent Cosmos read failure on evidence_bundles must not block the report."""
    handler, candidate_repo, verdict_repo, _, _, _, _ = _make_handler()
    candidate_repo.get_by_batch.return_value = [_make_candidate_dict()]
    verdict_repo.get_by_batch.return_value = [_make_verdict_dict()]
    handler._deps.evidence_repo.get_by_batch.side_effect = RuntimeError("Cosmos read failed")
    envelope = _make_envelope()

    outcome = await handler.handle(envelope)

    assert outcome == ProcessOutcome.SUCCESS


async def test_handle_storage_permanent_error_returns_permanent():
    handler, candidate_repo, verdict_repo, _, _, assemble_handler_mock, failed_publisher = (
        _make_handler()
    )
    candidate_repo.get_by_batch.return_value = [_make_candidate_dict()]
    verdict_repo.get_by_batch.return_value = [_make_verdict_dict()]
    assemble_handler_mock.handle.side_effect = StoragePermanentError("report too large")
    envelope = _make_envelope()

    outcome = await handler.handle(envelope)

    assert outcome == ProcessOutcome.PERMANENT
    failed_publisher.publish.assert_awaited_once()


async def test_handle_storage_permanent_error_publishes_harvesting_failed():
    handler, candidate_repo, verdict_repo, _, _, assemble_handler_mock, failed_publisher = (
        _make_handler()
    )
    candidate_repo.get_by_batch.return_value = [_make_candidate_dict()]
    verdict_repo.get_by_batch.return_value = [_make_verdict_dict()]
    assemble_handler_mock.handle.side_effect = StoragePermanentError("request too large")
    envelope = _make_envelope()

    await handler.handle(envelope)

    failed_publisher.publish.assert_awaited_once()
    topic, event = failed_publisher.publish.await_args.args
    assert topic == SETTINGS.harvesting_failed_topic
    assert event.event_type == "harvesting.failed"
    assert event.batch_id == BATCH_ID
