from unittest.mock import AsyncMock, MagicMock, patch

import pytest

from scoring.application.dtos.score_verdict_request import ScoreVerdictRequestDto
from scoring.application.handlers.score_verdict_handler import ScoreVerdictDeps, ScoreVerdictHandler
from scoring.application.prompt.grounded_prompt_builder import HitRenderConfig
from scoring.domain.enums.agreement_level import AgreementLevel
from scoring.domain.enums.scoring_axis import ScoringAxis
from scoring.domain.errors.storage_errors import EventPublishError, StorageWriteError
from scoring.domain.models.axis_score import AxisScore
from scoring.domain.models.dual_scoring_verdict import DualScoringVerdict
from scoring.domain.models.scoring_verdict import ScoringVerdict
from scoring.infrastructure.config.settings import ScoringSettings

_AXES = {
    ScoringAxis.Novelty: AxisScore(axis=ScoringAxis.Novelty, score=80, refs=["E1"]),
    ScoringAxis.Inventiveness: AxisScore(axis=ScoringAxis.Inventiveness, score=60, refs=["E1"]),
    ScoringAxis.Commercial: AxisScore(axis=ScoringAxis.Commercial, score=70, refs=["E1"]),
    ScoringAxis.Strategic: AxisScore(axis=ScoringAxis.Strategic, score=50, refs=["E1"]),
    ScoringAxis.Patentability: AxisScore(axis=ScoringAxis.Patentability, score=90, refs=["E1"]),
}

_VERDICT = ScoringVerdict(
    batch_id="batch-1",
    job_id="job-1",
    candidate_id="cand-1",
    document_id="doc-1",
    axes=_AXES,
)

_DUAL = DualScoringVerdict(
    primary=_VERDICT,
    secondary=None,
    agreement_level=AgreementLevel.SingleConfigured,
    agreeing_axis_count=0,
    single_reason="dual_mode_disabled",
)


def _make_settings(dual_mode: bool = False) -> ScoringSettings:
    return ScoringSettings(
        cosmos_uri="https://test.documents.azure.com:443/",
        servicebus_namespace_fqdn="test.servicebus.windows.net",
        model_router_url="http://cortexa-dev-model-router.internal.example.azurecontainerapps.io",
        dual_mode_enabled=dual_mode,
    )


def _make_deps(dual_mode: bool = False) -> ScoreVerdictDeps:
    return ScoreVerdictDeps(
        repository=AsyncMock(),
        publisher=AsyncMock(),
        settings=_make_settings(dual_mode),
        model_router_client=MagicMock(),
    )


def _make_request(bundle, ai_model: str | None = None) -> ScoreVerdictRequestDto:
    return ScoreVerdictRequestDto(
        candidate_description="A novel widget",
        bundle=bundle,
        correlation_id="corr-1",
        ai_model=ai_model,
    )


@pytest.mark.asyncio
async def test_single_mode_routes_to_single_model_scorer(bundle_all_sources):
    deps = _make_deps(dual_mode=False)
    handler = ScoreVerdictHandler(deps)

    with patch(
        "scoring.application.handlers.score_verdict_handler.SingleModelScorer"
    ) as MockSingle:
        MockSingle.return_value.score = AsyncMock(return_value=_DUAL)
        response = await handler.handle(_make_request(bundle_all_sources))

    MockSingle.assert_called_once_with(deps.model_router_client, HitRenderConfig())
    MockSingle.return_value.score.assert_awaited_once()
    assert response.published is True


@pytest.mark.asyncio
async def test_single_mode_forwards_ai_model_to_scorer(bundle_all_sources):
    deps = _make_deps(dual_mode=False)
    handler = ScoreVerdictHandler(deps)

    with patch(
        "scoring.application.handlers.score_verdict_handler.SingleModelScorer"
    ) as MockSingle:
        MockSingle.return_value.score = AsyncMock(return_value=_DUAL)
        await handler.handle(_make_request(bundle_all_sources, ai_model="gpt-5.4"))

    MockSingle.return_value.score.assert_awaited_once_with(
        "A novel widget", bundle_all_sources, "gpt-5.4"
    )


@pytest.mark.asyncio
async def test_dual_mode_routes_to_dual_model_scorer(bundle_all_sources):
    deps = _make_deps(dual_mode=True)
    handler = ScoreVerdictHandler(deps)

    with patch("scoring.application.handlers.score_verdict_handler.DualModelScorer") as MockDual:
        MockDual.return_value.score = AsyncMock(return_value=_DUAL)
        response = await handler.handle(_make_request(bundle_all_sources))

    MockDual.assert_called_once_with(deps.model_router_client, HitRenderConfig())
    MockDual.return_value.score.assert_awaited_once()
    assert response.published is True


@pytest.mark.asyncio
async def test_dual_mode_ignores_ai_model_and_keeps_fixed_pair(bundle_all_sources):
    deps = _make_deps(dual_mode=True)
    handler = ScoreVerdictHandler(deps)

    with patch("scoring.application.handlers.score_verdict_handler.DualModelScorer") as MockDual:
        MockDual.return_value.score = AsyncMock(return_value=_DUAL)
        await handler.handle(_make_request(bundle_all_sources, ai_model="gpt-5.4"))

    MockDual.return_value.score.assert_awaited_once_with("A novel widget", bundle_all_sources)


@pytest.mark.asyncio
async def test_storage_failure_raises_and_publish_not_called(bundle_all_sources):
    deps = _make_deps(dual_mode=False)
    deps.repository.save.side_effect = StorageWriteError("cosmos down")
    handler = ScoreVerdictHandler(deps)

    with patch(
        "scoring.application.handlers.score_verdict_handler.SingleModelScorer"
    ) as MockSingle:
        MockSingle.return_value.score = AsyncMock(return_value=_DUAL)
        with pytest.raises(StorageWriteError):
            await handler.handle(_make_request(bundle_all_sources))

    deps.publisher.publish.assert_not_awaited()


@pytest.mark.asyncio
async def test_publish_failure_raises_event_publish_error(bundle_all_sources):
    deps = _make_deps(dual_mode=False)
    deps.publisher.publish.side_effect = EventPublishError(
        "bus down", verdict_id="batch-1:cand-1", document_id="doc-1"
    )
    handler = ScoreVerdictHandler(deps)

    with patch(
        "scoring.application.handlers.score_verdict_handler.SingleModelScorer"
    ) as MockSingle:
        MockSingle.return_value.score = AsyncMock(return_value=_DUAL)
        with pytest.raises(EventPublishError):
            await handler.handle(_make_request(bundle_all_sources))

    deps.repository.save.assert_awaited_once()


@pytest.mark.asyncio
async def test_grounding_signal_threaded_from_bundle_to_verdict(bundle_all_sources):
    bundle_all_sources.active_source_count = 1
    bundle_all_sources.meets_minimum_sources = False
    bundle_all_sources.minimum_active_sources = 3
    deps = _make_deps(dual_mode=False)
    handler = ScoreVerdictHandler(deps)

    with patch(
        "scoring.application.handlers.score_verdict_handler.SingleModelScorer"
    ) as MockSingle:
        MockSingle.return_value.score = AsyncMock(return_value=_DUAL)
        await handler.handle(_make_request(bundle_all_sources))

    saved_verdict = deps.repository.save.await_args[0][0]
    assert saved_verdict.grounding_meets_minimum is False
    assert saved_verdict.grounding_source_count == 1


@pytest.mark.asyncio
async def test_grounding_happy_path_three_sources(bundle_all_sources):
    bundle_all_sources.active_source_count = 3
    bundle_all_sources.meets_minimum_sources = True
    bundle_all_sources.minimum_active_sources = 3
    deps = _make_deps(dual_mode=False)
    handler = ScoreVerdictHandler(deps)

    with patch(
        "scoring.application.handlers.score_verdict_handler.SingleModelScorer"
    ) as MockSingle:
        MockSingle.return_value.score = AsyncMock(return_value=_DUAL)
        await handler.handle(_make_request(bundle_all_sources))

    saved_verdict = deps.repository.save.await_args[0][0]
    assert saved_verdict.grounding_meets_minimum is True
    assert saved_verdict.grounding_source_count == 3


@pytest.mark.asyncio
async def test_single_reason_threaded_from_dual_scoring_verdict_to_stored_verdict(
    bundle_all_sources,
):
    deps = _make_deps(dual_mode=False)
    handler = ScoreVerdictHandler(deps)

    with patch(
        "scoring.application.handlers.score_verdict_handler.SingleModelScorer"
    ) as MockSingle:
        MockSingle.return_value.score = AsyncMock(return_value=_DUAL)
        await handler.handle(_make_request(bundle_all_sources))

    saved_verdict = deps.repository.save.await_args[0][0]
    assert saved_verdict.agreement_level == AgreementLevel.SingleConfigured
    assert saved_verdict.single_reason == "dual_mode_disabled"
