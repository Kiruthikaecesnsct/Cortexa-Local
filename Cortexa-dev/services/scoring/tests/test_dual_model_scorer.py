import json

import pytest

from scoring.application.dual_model_scorer import DualModelScorer
from scoring.domain.enums.agreement_level import AgreementLevel
from scoring.domain.errors.scoring_errors import AxisParseError, DualScoringFailedError
from scoring.infrastructure.clients.dual_response import DualCompleteResponse, ModelResult
from scoring.infrastructure.clients.model_router_client import ModelRouterClient

_VALID_LLM_JSON = json.dumps(
    {
        "Novelty": {"score": 80, "refs": ["E1"]},
        "Inventiveness": {"score": 70, "refs": ["E2"]},
        "Commercial": {"score": 60, "refs": ["E3"]},
        "Strategic": {"score": 50, "refs": ["E1"]},
        "Patentability": {"score": 90, "refs": ["E2"]},
    }
)

_CANDIDATE_DESCRIPTION = "A novel method for lossless compression of genomic data."


def _make_dual_response(
    primary_content: str = _VALID_LLM_JSON,
    secondary_content: str = _VALID_LLM_JSON,
    secondary_error: str | None = None,
) -> DualCompleteResponse:
    return DualCompleteResponse(
        primary=ModelResult(provider="azure", model="gpt-4o", content=primary_content),
        secondary=ModelResult(
            provider="anthropic",
            model="claude-3-5-sonnet",
            content=secondary_content,
            error=secondary_error,
        ),
    )


@pytest.mark.asyncio
async def test_score_happy_path_returns_dual_scoring_verdict_with_agreement_level(
    mocker, bundle_all_sources
):
    mock_client = mocker.AsyncMock(spec=ModelRouterClient)
    mock_client.complete_dual.return_value = _make_dual_response()
    scorer = DualModelScorer(client=mock_client)

    result = await scorer.score(_CANDIDATE_DESCRIPTION, bundle_all_sources)

    assert result.primary is not None
    assert result.secondary is not None
    assert result.agreement_level in list(AgreementLevel)
    assert result.agreement_level != AgreementLevel.FallbackSingle
    assert isinstance(result.agreeing_axis_count, int)
    assert result.single_reason is None


@pytest.mark.asyncio
async def test_score_secondary_timeout_error_returns_fallback_single_with_timeout_reason(
    mocker, bundle_all_sources
):
    mock_client = mocker.AsyncMock(spec=ModelRouterClient)
    mock_client.complete_dual.return_value = _make_dual_response(
        secondary_content="", secondary_error="model timeout"
    )
    scorer = DualModelScorer(client=mock_client)

    result = await scorer.score(_CANDIDATE_DESCRIPTION, bundle_all_sources)

    assert result.agreement_level == AgreementLevel.FallbackSingle
    assert result.secondary is None
    assert result.agreeing_axis_count == 0
    assert result.single_reason == "secondary_timeout"


@pytest.mark.asyncio
async def test_score_secondary_generic_error_returns_fallback_single_with_unavailable_reason(
    mocker, bundle_all_sources
):
    mock_client = mocker.AsyncMock(spec=ModelRouterClient)
    mock_client.complete_dual.return_value = _make_dual_response(
        secondary_content="", secondary_error="503 service unavailable"
    )
    scorer = DualModelScorer(client=mock_client)

    result = await scorer.score(_CANDIDATE_DESCRIPTION, bundle_all_sources)

    assert result.agreement_level == AgreementLevel.FallbackSingle
    assert result.secondary is None
    assert result.agreeing_axis_count == 0
    assert result.single_reason == "secondary_unavailable"


@pytest.mark.asyncio
async def test_score_secondary_content_empty_returns_fallback_single_with_unavailable_reason(
    mocker, bundle_all_sources
):
    mock_client = mocker.AsyncMock(spec=ModelRouterClient)
    mock_client.complete_dual.return_value = _make_dual_response(
        secondary_content="", secondary_error=None
    )
    scorer = DualModelScorer(client=mock_client)

    result = await scorer.score(_CANDIDATE_DESCRIPTION, bundle_all_sources)

    assert result.agreement_level == AgreementLevel.FallbackSingle
    assert result.secondary is None
    assert result.agreeing_axis_count == 0
    assert result.single_reason == "secondary_unavailable"


@pytest.mark.asyncio
async def test_score_secondary_unparseable_json_returns_fallback_single_with_parse_failed_reason(
    mocker, bundle_all_sources
):
    mock_client = mocker.AsyncMock(spec=ModelRouterClient)
    mock_client.complete_dual.return_value = _make_dual_response(
        secondary_content="not valid json at all {{{"
    )
    scorer = DualModelScorer(client=mock_client)

    result = await scorer.score(_CANDIDATE_DESCRIPTION, bundle_all_sources)

    assert result.agreement_level == AgreementLevel.FallbackSingle
    assert result.secondary is None
    assert result.agreeing_axis_count == 0
    assert result.single_reason == "secondary_parse_failed"


@pytest.mark.asyncio
async def test_score_primary_parse_failure_propagates_axis_parse_error(mocker, bundle_all_sources):
    mock_client = mocker.AsyncMock(spec=ModelRouterClient)
    mock_client.complete_dual.return_value = _make_dual_response(
        primary_content="completely unparseable !!!"
    )
    scorer = DualModelScorer(client=mock_client)

    with pytest.raises(AxisParseError):
        await scorer.score(_CANDIDATE_DESCRIPTION, bundle_all_sources)


@pytest.mark.asyncio
async def test_score_both_models_fail_propagates_dual_scoring_failed_error(
    mocker, bundle_all_sources
):
    mock_client = mocker.AsyncMock(spec=ModelRouterClient)
    mock_client.complete_dual.side_effect = DualScoringFailedError("both models failed")
    scorer = DualModelScorer(client=mock_client)

    with pytest.raises(DualScoringFailedError):
        await scorer.score(_CANDIDATE_DESCRIPTION, bundle_all_sources)
