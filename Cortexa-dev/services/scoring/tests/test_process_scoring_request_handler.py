from unittest.mock import AsyncMock, MagicMock

import pytest

from scoring.application.handlers.process_scoring_request_handler import (
    ProcessOutcome,
    ProcessScoringDeps,
    ProcessScoringRequestHandler,
    _classify_error,
)
from scoring.domain.enums.agreement_level import AgreementLevel
from scoring.domain.errors.scoring_errors import (
    AxisParseError,
    DualScoringFailedError,
    SingleScoringFailedError,
    UngroundedVerdictError,
)
from scoring.domain.errors.storage_errors import (
    EventPublishError,
    StorageWriteError,
    VerdictNotFoundError,
)
from scoring.domain.events.event_envelope import EventEnvelope
from scoring.domain.models.stored_verdict import StoredVerdict
from scoring.infrastructure.config.settings import ScoringSettings

_SETTINGS = ScoringSettings(
    cosmos_uri="https://test.documents.azure.com:443/",
    servicebus_namespace_fqdn="test.servicebus.windows.net",
    model_router_url="http://cortexa-dev-model-router.internal.example.azurecontainerapps.io",
)

_STORED_VERDICT = StoredVerdict(
    id="batch-1:cand-1",
    batch_id="batch-1",
    job_id="job-1",
    candidate_id="cand-1",
    document_id="doc-1",
    axes={},
    composite_score=0.75,
    agreement_level=AgreementLevel.FallbackSingle,
    agreeing_axis_count=0,
)


def _make_envelope(
    candidate_id: str = "cand-1", evidence_id: str = "ev-1", ai_model: str | None = None
) -> EventEnvelope:
    payload = {"candidate_id": candidate_id, "evidence_bundle_id": evidence_id}
    if ai_model is not None:
        payload["ai_model"] = ai_model
    return EventEnvelope(
        event_type="scoring.requested",
        batch_id="batch-1",
        document_id="doc-1",
        correlation_id="corr-1",
        payload=payload,
    )


def _make_candidate(claim_text: str = "A novel widget") -> MagicMock:
    candidate = MagicMock()
    candidate.claim_text = claim_text
    return candidate


def _make_deps(
    verdict_repo: AsyncMock | None = None,
    candidate_reader: AsyncMock | None = None,
    bundle_reader: AsyncMock | None = None,
    score_handler: AsyncMock | None = None,
    publisher: AsyncMock | None = None,
) -> ProcessScoringDeps:
    return ProcessScoringDeps(
        verdict_repo=verdict_repo or AsyncMock(),
        candidate_reader=candidate_reader or AsyncMock(),
        bundle_reader=bundle_reader or AsyncMock(),
        score_handler=score_handler or AsyncMock(),
        publisher=publisher or AsyncMock(),
        settings=_SETTINGS,
    )


@pytest.mark.asyncio
async def test_missing_candidate_id_returns_permanent():
    envelope = EventEnvelope(
        event_type="scoring.requested",
        batch_id="batch-1",
        document_id="doc-1",
        correlation_id="corr-1",
        payload={},
    )
    handler = ProcessScoringRequestHandler(_make_deps())

    outcome = await handler.handle(envelope)

    assert outcome == ProcessOutcome.PERMANENT


@pytest.mark.asyncio
async def test_missing_candidate_id_publishes_scoring_failed():
    envelope = EventEnvelope(
        event_type="scoring.requested",
        batch_id="batch-1",
        document_id="doc-1",
        correlation_id="corr-1",
        payload={},
    )
    publisher = AsyncMock()
    deps = _make_deps(publisher=publisher)
    handler = ProcessScoringRequestHandler(deps)

    outcome = await handler.handle(envelope)

    assert outcome == ProcessOutcome.PERMANENT
    publisher.publish.assert_awaited_once()
    topic, event = publisher.publish.await_args.args
    assert topic == _SETTINGS.scoring_failed_topic
    assert event.event_type == "scoring.failed"
    assert event.payload["reason"] == "missing_candidate_id"
    assert event.payload["document_id"] == "doc-1"
    assert event.payload["job_id"] == "batch-1"
    assert event.payload["candidate_id"] is None
    assert event.payload["trigger_type"] == "pipeline"


@pytest.mark.asyncio
async def test_axis_parse_error_publishes_scoring_failed(bundle_all_sources):
    verdict_repo = AsyncMock()
    verdict_repo.find_by_candidate.return_value = None
    candidate_reader = AsyncMock()
    candidate_reader.get_by_candidate_id.return_value = _make_candidate()
    bundle_reader = AsyncMock()
    bundle_reader.get_domain_bundle.return_value = bundle_all_sources
    score_handler = AsyncMock()
    score_handler.handle.side_effect = AxisParseError("bad axis")
    publisher = AsyncMock()
    deps = _make_deps(
        verdict_repo=verdict_repo,
        candidate_reader=candidate_reader,
        bundle_reader=bundle_reader,
        score_handler=score_handler,
        publisher=publisher,
    )
    handler = ProcessScoringRequestHandler(deps)

    outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.PERMANENT
    publisher.publish.assert_awaited_once()
    topic, event = publisher.publish.await_args.args
    assert topic == _SETTINGS.scoring_failed_topic
    assert event.event_type == "scoring.failed"
    assert event.payload["candidate_id"] == "cand-1"


@pytest.mark.asyncio
async def test_transient_error_does_not_publish_scoring_failed(bundle_all_sources):
    verdict_repo = AsyncMock()
    verdict_repo.find_by_candidate.return_value = None
    candidate_reader = AsyncMock()
    candidate_reader.get_by_candidate_id.return_value = _make_candidate()
    bundle_reader = AsyncMock()
    bundle_reader.get_domain_bundle.return_value = bundle_all_sources
    score_handler = AsyncMock()
    score_handler.handle.side_effect = SingleScoringFailedError("model down")
    publisher = AsyncMock()
    deps = _make_deps(
        verdict_repo=verdict_repo,
        candidate_reader=candidate_reader,
        bundle_reader=bundle_reader,
        score_handler=score_handler,
        publisher=publisher,
    )
    handler = ProcessScoringRequestHandler(deps)

    outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.TRANSIENT
    publisher.publish.assert_not_awaited()


@pytest.mark.asyncio
async def test_idempotent_path_republishes_and_returns_success():
    verdict_repo = AsyncMock()
    verdict_repo.find_by_candidate.return_value = _STORED_VERDICT
    publisher = AsyncMock()
    deps = _make_deps(verdict_repo=verdict_repo, publisher=publisher)
    handler = ProcessScoringRequestHandler(deps)

    outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.SUCCESS
    publisher.publish.assert_awaited_once()
    deps.score_handler.handle.assert_not_awaited()


@pytest.mark.asyncio
async def test_idempotent_publish_failure_returns_transient():
    verdict_repo = AsyncMock()
    verdict_repo.find_by_candidate.return_value = _STORED_VERDICT
    publisher = AsyncMock()
    publisher.publish.side_effect = EventPublishError("bus down")
    deps = _make_deps(verdict_repo=verdict_repo, publisher=publisher)
    handler = ProcessScoringRequestHandler(deps)

    outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.TRANSIENT


@pytest.mark.asyncio
async def test_idempotent_recovery_after_publish_failure(bundle_all_sources):
    verdict_repo = AsyncMock()
    publisher = AsyncMock()
    score_handler = AsyncMock()
    candidate_reader = AsyncMock()
    candidate_reader.get_by_candidate_id.return_value = _make_candidate()
    bundle_reader = AsyncMock()
    bundle_reader.get_domain_bundle.return_value = bundle_all_sources
    deps = _make_deps(
        verdict_repo=verdict_repo,
        candidate_reader=candidate_reader,
        bundle_reader=bundle_reader,
        publisher=publisher,
        score_handler=score_handler,
    )
    handler = ProcessScoringRequestHandler(deps)
    envelope = _make_envelope()

    verdict_repo.find_by_candidate.return_value = None
    score_handler.handle.side_effect = EventPublishError("bus down")

    first_outcome = await handler.handle(envelope)

    assert first_outcome == ProcessOutcome.TRANSIENT

    verdict_repo.find_by_candidate.return_value = _STORED_VERDICT
    score_handler.handle.side_effect = None

    second_outcome = await handler.handle(envelope)

    assert second_outcome == ProcessOutcome.SUCCESS
    publisher.publish.assert_awaited_once()
    score_handler.handle.assert_awaited_once()


@pytest.mark.asyncio
async def test_ai_model_extracted_from_envelope_and_forwarded(bundle_all_sources):
    verdict_repo = AsyncMock()
    verdict_repo.find_by_candidate.return_value = None
    candidate_reader = AsyncMock()
    candidate_reader.get_by_candidate_id.return_value = _make_candidate()
    bundle_reader = AsyncMock()
    bundle_reader.get_domain_bundle.return_value = bundle_all_sources
    score_handler = AsyncMock()
    deps = _make_deps(
        verdict_repo=verdict_repo,
        candidate_reader=candidate_reader,
        bundle_reader=bundle_reader,
        score_handler=score_handler,
    )
    handler = ProcessScoringRequestHandler(deps)

    outcome = await handler.handle(_make_envelope(ai_model="gpt-5.4"))

    assert outcome == ProcessOutcome.SUCCESS
    forwarded_request = score_handler.handle.await_args.args[0]
    assert forwarded_request.ai_model == "gpt-5.4"


@pytest.mark.asyncio
async def test_ai_model_absent_from_envelope_defaults_to_none(bundle_all_sources):
    verdict_repo = AsyncMock()
    verdict_repo.find_by_candidate.return_value = None
    candidate_reader = AsyncMock()
    candidate_reader.get_by_candidate_id.return_value = _make_candidate()
    bundle_reader = AsyncMock()
    bundle_reader.get_domain_bundle.return_value = bundle_all_sources
    score_handler = AsyncMock()
    deps = _make_deps(
        verdict_repo=verdict_repo,
        candidate_reader=candidate_reader,
        bundle_reader=bundle_reader,
        score_handler=score_handler,
    )
    handler = ProcessScoringRequestHandler(deps)

    outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.SUCCESS
    forwarded_request = score_handler.handle.await_args.args[0]
    assert forwarded_request.ai_model is None


@pytest.mark.asyncio
async def test_bundle_not_ready_returns_transient():
    verdict_repo = AsyncMock()
    verdict_repo.find_by_candidate.return_value = None
    candidate_reader = AsyncMock()
    candidate_reader.get_by_candidate_id.return_value = _make_candidate()
    bundle_reader = AsyncMock()
    bundle_reader.get_domain_bundle.return_value = None
    deps = _make_deps(
        verdict_repo=verdict_repo,
        candidate_reader=candidate_reader,
        bundle_reader=bundle_reader,
    )
    handler = ProcessScoringRequestHandler(deps)

    outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.TRANSIENT


@pytest.mark.asyncio
async def test_candidate_not_found_returns_permanent():
    verdict_repo = AsyncMock()
    verdict_repo.find_by_candidate.return_value = None
    candidate_reader = AsyncMock()
    candidate_reader.get_by_candidate_id.side_effect = VerdictNotFoundError("cand-1")
    deps = _make_deps(verdict_repo=verdict_repo, candidate_reader=candidate_reader)
    handler = ProcessScoringRequestHandler(deps)

    outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.PERMANENT


@pytest.mark.asyncio
async def test_axis_parse_error_returns_permanent(bundle_all_sources):
    verdict_repo = AsyncMock()
    verdict_repo.find_by_candidate.return_value = None
    candidate_reader = AsyncMock()
    candidate_reader.get_by_candidate_id.return_value = _make_candidate()
    bundle_reader = AsyncMock()
    bundle_reader.get_domain_bundle.return_value = bundle_all_sources
    score_handler = AsyncMock()
    score_handler.handle.side_effect = AxisParseError("bad axis")
    deps = _make_deps(
        verdict_repo=verdict_repo,
        candidate_reader=candidate_reader,
        bundle_reader=bundle_reader,
        score_handler=score_handler,
    )
    handler = ProcessScoringRequestHandler(deps)

    outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.PERMANENT


@pytest.mark.asyncio
async def test_ungrounded_verdict_error_returns_permanent(bundle_all_sources):
    verdict_repo = AsyncMock()
    verdict_repo.find_by_candidate.return_value = None
    candidate_reader = AsyncMock()
    candidate_reader.get_by_candidate_id.return_value = _make_candidate()
    bundle_reader = AsyncMock()
    bundle_reader.get_domain_bundle.return_value = bundle_all_sources
    score_handler = AsyncMock()
    score_handler.handle.side_effect = UngroundedVerdictError()
    deps = _make_deps(
        verdict_repo=verdict_repo,
        candidate_reader=candidate_reader,
        bundle_reader=bundle_reader,
        score_handler=score_handler,
    )
    handler = ProcessScoringRequestHandler(deps)

    outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.PERMANENT


@pytest.mark.asyncio
async def test_single_scoring_failed_returns_transient(bundle_all_sources):
    verdict_repo = AsyncMock()
    verdict_repo.find_by_candidate.return_value = None
    candidate_reader = AsyncMock()
    candidate_reader.get_by_candidate_id.return_value = _make_candidate()
    bundle_reader = AsyncMock()
    bundle_reader.get_domain_bundle.return_value = bundle_all_sources
    score_handler = AsyncMock()
    score_handler.handle.side_effect = SingleScoringFailedError("model down")
    deps = _make_deps(
        verdict_repo=verdict_repo,
        candidate_reader=candidate_reader,
        bundle_reader=bundle_reader,
        score_handler=score_handler,
    )
    handler = ProcessScoringRequestHandler(deps)

    outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.TRANSIENT


@pytest.mark.asyncio
async def test_dual_scoring_failed_returns_transient(bundle_all_sources):
    verdict_repo = AsyncMock()
    verdict_repo.find_by_candidate.return_value = None
    candidate_reader = AsyncMock()
    candidate_reader.get_by_candidate_id.return_value = _make_candidate()
    bundle_reader = AsyncMock()
    bundle_reader.get_domain_bundle.return_value = bundle_all_sources
    score_handler = AsyncMock()
    score_handler.handle.side_effect = DualScoringFailedError()
    deps = _make_deps(
        verdict_repo=verdict_repo,
        candidate_reader=candidate_reader,
        bundle_reader=bundle_reader,
        score_handler=score_handler,
    )
    handler = ProcessScoringRequestHandler(deps)

    outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.TRANSIENT


@pytest.mark.asyncio
async def test_storage_write_error_returns_transient(bundle_all_sources):
    verdict_repo = AsyncMock()
    verdict_repo.find_by_candidate.return_value = None
    candidate_reader = AsyncMock()
    candidate_reader.get_by_candidate_id.return_value = _make_candidate()
    bundle_reader = AsyncMock()
    bundle_reader.get_domain_bundle.return_value = bundle_all_sources
    score_handler = AsyncMock()
    score_handler.handle.side_effect = StorageWriteError("cosmos down")
    deps = _make_deps(
        verdict_repo=verdict_repo,
        candidate_reader=candidate_reader,
        bundle_reader=bundle_reader,
        score_handler=score_handler,
    )
    handler = ProcessScoringRequestHandler(deps)

    outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.TRANSIENT


@pytest.mark.asyncio
async def test_candidate_description_too_long_returns_permanent(bundle_all_sources):
    verdict_repo = AsyncMock()
    verdict_repo.find_by_candidate.return_value = None
    candidate_reader = AsyncMock()
    candidate_reader.get_by_candidate_id.return_value = _make_candidate(claim_text="x" * 4001)
    bundle_reader = AsyncMock()
    bundle_reader.get_domain_bundle.return_value = bundle_all_sources
    deps = _make_deps(
        verdict_repo=verdict_repo,
        candidate_reader=candidate_reader,
        bundle_reader=bundle_reader,
    )
    handler = ProcessScoringRequestHandler(deps)

    outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.PERMANENT


@pytest.mark.parametrize(
    "exc",
    [
        TypeError("ClientSession._request() got an unexpected keyword argument 'partition_key'"),
        AttributeError("x"),
        KeyError("candidate_id"),
        NotImplementedError(),
        ImportError("no module"),
    ],
)
def test_classify_error_programming_errors_are_permanent(exc):
    assert _classify_error(exc) == ProcessOutcome.PERMANENT


@pytest.mark.parametrize(
    "exc",
    [
        AxisParseError("bad axis"),
        UngroundedVerdictError("no citations"),
        VerdictNotFoundError("cand-1"),
    ],
)
def test_classify_error_domain_permanent_errors_stay_permanent(exc):
    assert _classify_error(exc) == ProcessOutcome.PERMANENT


@pytest.mark.parametrize(
    "exc",
    [
        EventPublishError("publish"),
        StorageWriteError("write"),
    ],
)
def test_classify_error_transient_errors_stay_transient(exc):
    assert _classify_error(exc) == ProcessOutcome.TRANSIENT


def test_classify_error_single_scoring_failed_with_400_is_permanent():
    assert (
        _classify_error(SingleScoringFailedError("bad request", status_code=400))
        == ProcessOutcome.PERMANENT
    )


def test_classify_error_dual_scoring_failed_with_400_is_permanent():
    assert (
        _classify_error(DualScoringFailedError("bad request", status_code=400))
        == ProcessOutcome.PERMANENT
    )


def test_classify_error_single_scoring_failed_with_422_is_permanent():
    assert (
        _classify_error(SingleScoringFailedError("unprocessable", status_code=422))
        == ProcessOutcome.PERMANENT
    )


def test_classify_error_single_scoring_failed_with_500_is_transient():
    assert (
        _classify_error(SingleScoringFailedError("server error", status_code=500))
        == ProcessOutcome.TRANSIENT
    )


def test_classify_error_dual_scoring_failed_with_502_is_transient():
    assert (
        _classify_error(DualScoringFailedError("bad gateway", status_code=502))
        == ProcessOutcome.TRANSIENT
    )


def test_classify_error_single_scoring_failed_no_status_code_is_transient():
    assert _classify_error(SingleScoringFailedError("unknown")) == ProcessOutcome.TRANSIENT


def test_classify_error_dual_scoring_failed_no_status_code_is_transient():
    assert _classify_error(DualScoringFailedError("unknown")) == ProcessOutcome.TRANSIENT


def test_classify_error_unknown_exception_defaults_to_transient():
    assert _classify_error(RuntimeError("unknown")) == ProcessOutcome.TRANSIENT
