from unittest.mock import AsyncMock, MagicMock

import pytest

from scoring.application.claim_draft_generator import (
    ClaimDraftGenerator,
    ClaimDraftRequest,
    ClaimDraftResult,
    ClaimDraftStatus,
)
from scoring.application.handlers.process_scoring_request_handler import (
    ProcessOutcome,
    ProcessScoringDeps,
    ProcessScoringRequestHandler,
)
from scoring.domain.enums.agreement_level import AgreementLevel
from scoring.domain.events.event_envelope import EventEnvelope
from scoring.domain.models.stored_verdict import StoredVerdict
from scoring.infrastructure.config.settings import ScoringSettings

_SETTINGS = ScoringSettings(
    cosmos_uri="https://test.documents.azure.com:443/",
    servicebus_namespace_fqdn="test.servicebus.windows.net",
    model_router_url="http://cortexa-dev-model-router.internal.example.azurecontainerapps.io",
)

_VERDICT_ID = "batch-1:cand-1"
_BATCH_ID = "batch-1"
_JOB_ID = "job-1"
_CANDIDATE_ID = "cand-1"
_DOCUMENT_ID = "doc-1"
_CLAIM_TEXT = "A novel widget comprising a mechanism"
_PROBLEM = "Existing widgets are inefficient"
_MECHANISM = "A rotary actuator with feedback control"
_TECH_FIELD = "Mechanical devices"
_COMPOSITE_SCORE = 0.75
_AXES_DICT = {}
_DRAFT_TEXT = (
    "A system comprising:\na first component configured to receive input;\n"
    "a second component coupled to the first component."
)


def _make_envelope(
    candidate_id: str = _CANDIDATE_ID, evidence_id: str = "ev-1", ai_model: str | None = None
) -> EventEnvelope:
    payload = {"candidate_id": candidate_id, "evidence_bundle_id": evidence_id}
    if ai_model is not None:
        payload["ai_model"] = ai_model
    return EventEnvelope(
        event_type="scoring.requested",
        batch_id=_BATCH_ID,
        document_id=_DOCUMENT_ID,
        correlation_id="corr-1",
        payload=payload,
    )


def _make_candidate(
    claim_text: str = _CLAIM_TEXT,
    problem: str = _PROBLEM,
    mechanism: str = _MECHANISM,
    tech_field: str = _TECH_FIELD,
) -> MagicMock:
    candidate = MagicMock()
    candidate.claim_text = claim_text
    candidate.problem = problem
    candidate.mechanism = mechanism
    candidate.tech_field = tech_field
    return candidate


def _make_base_verdict(drafted_claim: str = "", claim_draft_status: str = "") -> StoredVerdict:
    return StoredVerdict(
        id=_VERDICT_ID,
        batch_id=_BATCH_ID,
        job_id=_JOB_ID,
        candidate_id=_CANDIDATE_ID,
        document_id=_DOCUMENT_ID,
        axes=_AXES_DICT,
        composite_score=_COMPOSITE_SCORE,
        agreement_level=AgreementLevel.FallbackSingle,
        agreeing_axis_count=0,
        drafted_claim=drafted_claim,
        claim_draft_status=claim_draft_status,
    )


def _make_deps(
    verdict_repo: AsyncMock | None = None,
    candidate_reader: AsyncMock | None = None,
    bundle_reader: AsyncMock | None = None,
    score_handler: AsyncMock | None = None,
    publisher: AsyncMock | None = None,
    claim_draft_generator: ClaimDraftGenerator | None = None,
) -> ProcessScoringDeps:
    return ProcessScoringDeps(
        verdict_repo=verdict_repo or AsyncMock(),
        candidate_reader=candidate_reader or AsyncMock(),
        bundle_reader=bundle_reader or AsyncMock(),
        score_handler=score_handler or AsyncMock(),
        publisher=publisher or AsyncMock(),
        settings=_SETTINGS,
        claim_draft_generator=claim_draft_generator,
    )


@pytest.mark.asyncio
async def test_claim_draft_disabled_verdict_unchanged(bundle_all_sources):
    base_verdict = _make_base_verdict()

    verdict_repo = AsyncMock()
    verdict_repo.find_by_candidate.return_value = None
    candidate_reader = AsyncMock()
    candidate_reader.get_by_candidate_id.return_value = _make_candidate()
    bundle_reader = AsyncMock()
    bundle_reader.get_domain_bundle.return_value = bundle_all_sources
    publisher = AsyncMock()

    score_handler = AsyncMock()

    async def mock_score_handler_handle(request):
        await verdict_repo.save(base_verdict)
        await publisher.publish(_SETTINGS.scoring_completed_topic, _make_envelope())
        from scoring.application.dtos.store_verdict_response import StoreVerdictResponseDto

        return StoreVerdictResponseDto(
            verdict_id=_VERDICT_ID, document_id=_DOCUMENT_ID, published=True
        )

    score_handler.handle.side_effect = mock_score_handler_handle

    deps = _make_deps(
        verdict_repo=verdict_repo,
        candidate_reader=candidate_reader,
        bundle_reader=bundle_reader,
        score_handler=score_handler,
        publisher=publisher,
        claim_draft_generator=None,
    )
    handler = ProcessScoringRequestHandler(deps)

    outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.SUCCESS
    score_handler.handle.assert_awaited_once()
    assert verdict_repo.save.await_count == 1
    saved_verdict = verdict_repo.save.await_args.args[0]
    assert saved_verdict.drafted_claim == ""
    assert saved_verdict.claim_draft_status == ""
    assert saved_verdict.composite_score == _COMPOSITE_SCORE
    assert saved_verdict.axes == _AXES_DICT


@pytest.mark.asyncio
async def test_claim_draft_success_persists_to_verdict(bundle_all_sources):
    base_verdict = _make_base_verdict()
    find_call_count = 0

    verdict_repo = AsyncMock()

    async def find_by_candidate_side_effect(candidate_id: str, batch_id: str):
        nonlocal find_call_count
        find_call_count += 1
        if find_call_count == 1:
            return None
        return base_verdict

    verdict_repo.find_by_candidate.side_effect = find_by_candidate_side_effect

    candidate_reader = AsyncMock()
    candidate_reader.get_by_candidate_id.return_value = _make_candidate()
    bundle_reader = AsyncMock()
    bundle_reader.get_domain_bundle.return_value = bundle_all_sources
    publisher = AsyncMock()

    score_handler = AsyncMock()

    async def mock_score_handler_handle(request):
        await verdict_repo.save(base_verdict)
        await publisher.publish(_SETTINGS.scoring_completed_topic, _make_envelope())
        from scoring.application.dtos.store_verdict_response import StoreVerdictResponseDto

        return StoreVerdictResponseDto(
            verdict_id=_VERDICT_ID, document_id=_DOCUMENT_ID, published=True
        )

    score_handler.handle.side_effect = mock_score_handler_handle

    mock_generator = AsyncMock(spec=ClaimDraftGenerator)
    mock_generator.generate.return_value = ClaimDraftResult(
        drafted_claim=_DRAFT_TEXT, status=ClaimDraftStatus.DRAFTED
    )

    deps = _make_deps(
        verdict_repo=verdict_repo,
        candidate_reader=candidate_reader,
        bundle_reader=bundle_reader,
        score_handler=score_handler,
        publisher=publisher,
        claim_draft_generator=mock_generator,
    )
    handler = ProcessScoringRequestHandler(deps)

    outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.SUCCESS
    score_handler.handle.assert_awaited_once()
    mock_generator.generate.assert_awaited_once()
    draft_request = mock_generator.generate.await_args.args[0]
    assert isinstance(draft_request, ClaimDraftRequest)
    assert draft_request.claim_text == _CLAIM_TEXT
    assert draft_request.problem == _PROBLEM
    assert draft_request.mechanism == _MECHANISM
    assert draft_request.tech_field == _TECH_FIELD

    assert verdict_repo.save.await_count == 2
    saved_verdict_draft = verdict_repo.save.await_args.args[0]
    assert saved_verdict_draft.drafted_claim == _DRAFT_TEXT
    assert saved_verdict_draft.claim_draft_status == "drafted"
    assert saved_verdict_draft.composite_score == _COMPOSITE_SCORE
    assert saved_verdict_draft.axes == _AXES_DICT
    assert saved_verdict_draft.candidate_id == _CANDIDATE_ID


@pytest.mark.asyncio
async def test_claim_draft_failure_does_not_break_verdict(bundle_all_sources):
    base_verdict = _make_base_verdict()
    find_call_count = 0

    verdict_repo = AsyncMock()

    async def find_by_candidate_side_effect(candidate_id: str, batch_id: str):
        nonlocal find_call_count
        find_call_count += 1
        if find_call_count == 1:
            return None
        return base_verdict

    verdict_repo.find_by_candidate.side_effect = find_by_candidate_side_effect

    candidate_reader = AsyncMock()
    candidate_reader.get_by_candidate_id.return_value = _make_candidate()
    bundle_reader = AsyncMock()
    bundle_reader.get_domain_bundle.return_value = bundle_all_sources
    publisher = AsyncMock()

    score_handler = AsyncMock()

    async def mock_score_handler_handle(request):
        await verdict_repo.save(base_verdict)
        await publisher.publish(_SETTINGS.scoring_completed_topic, _make_envelope())
        from scoring.application.dtos.store_verdict_response import StoreVerdictResponseDto

        return StoreVerdictResponseDto(
            verdict_id=_VERDICT_ID, document_id=_DOCUMENT_ID, published=True
        )

    score_handler.handle.side_effect = mock_score_handler_handle

    mock_generator = AsyncMock(spec=ClaimDraftGenerator)
    mock_generator.generate.side_effect = TimeoutError("LLM request timed out")

    deps = _make_deps(
        verdict_repo=verdict_repo,
        candidate_reader=candidate_reader,
        bundle_reader=bundle_reader,
        score_handler=score_handler,
        publisher=publisher,
        claim_draft_generator=mock_generator,
    )
    handler = ProcessScoringRequestHandler(deps)

    outcome = await handler.handle(_make_envelope())

    assert outcome == ProcessOutcome.SUCCESS
    score_handler.handle.assert_awaited_once()
    mock_generator.generate.assert_awaited_once()

    assert verdict_repo.save.await_count == 1
    saved_verdict = verdict_repo.save.await_args.args[0]
    assert saved_verdict.drafted_claim == ""
    assert saved_verdict.claim_draft_status == ""
    assert saved_verdict.composite_score == _COMPOSITE_SCORE
    assert saved_verdict.axes == _AXES_DICT

    publisher.publish.assert_awaited_once()
