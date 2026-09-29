from unittest.mock import AsyncMock, MagicMock

import pytest

from scoring.application.single_model_scorer import SingleModelScorer
from scoring.domain.enums.agreement_level import AgreementLevel
from scoring.domain.enums.scoring_axis import ScoringAxis
from scoring.domain.errors.scoring_errors import SingleScoringFailedError
from scoring.infrastructure.clients.single_response import SingleCompleteResponse

_VALID_RESPONSE = """{
  "Novelty": {"score": 80, "refs": ["E1"]},
  "Inventiveness": {"score": 60, "refs": ["E1"]},
  "Commercial": {"score": 70, "refs": ["E1"]},
  "Strategic": {"score": 50, "refs": ["E1"]},
  "Patentability": {"score": 90, "refs": ["E1"]}
}"""


def _make_client(content: str, error: str | None = None) -> MagicMock:
    client = MagicMock()
    client.complete_single = AsyncMock(
        return_value=SingleCompleteResponse(
            provider="azure-foundry",
            model="gpt-4o",
            content=content,
            error=error,
        )
    )
    return client


@pytest.mark.asyncio
async def test_single_scorer_returns_single_configured_verdict(bundle_all_sources):
    client = _make_client(_VALID_RESPONSE)
    scorer = SingleModelScorer(client)

    result = await scorer.score("A novel invention", bundle_all_sources)

    assert result.agreement_level == AgreementLevel.SingleConfigured
    assert result.secondary is None
    assert result.agreeing_axis_count == 0
    assert result.single_reason == "dual_mode_disabled"
    assert ScoringAxis.Novelty in result.primary.axes
    client.complete_single.assert_awaited_once()


@pytest.mark.asyncio
async def test_single_scorer_raises_on_model_error(bundle_all_sources):
    client = _make_client(content="", error="upstream timeout")
    scorer = SingleModelScorer(client)

    with pytest.raises(SingleScoringFailedError, match="upstream timeout"):
        await scorer.score("A novel invention", bundle_all_sources)


@pytest.mark.asyncio
async def test_single_scorer_raises_on_empty_content(bundle_all_sources):
    client = _make_client(content="", error=None)
    scorer = SingleModelScorer(client)

    with pytest.raises(SingleScoringFailedError, match="empty response"):
        await scorer.score("A novel invention", bundle_all_sources)


@pytest.mark.asyncio
async def test_single_scorer_forwards_ai_model_to_client(bundle_all_sources):
    client = _make_client(_VALID_RESPONSE)
    scorer = SingleModelScorer(client)

    await scorer.score("A novel invention", bundle_all_sources, ai_model="gpt-5.4")

    client.complete_single.assert_awaited_once_with(
        client.complete_single.await_args.args[0], "gpt-5.4"
    )


@pytest.mark.asyncio
async def test_single_scorer_defaults_ai_model_to_none(bundle_all_sources):
    client = _make_client(_VALID_RESPONSE)
    scorer = SingleModelScorer(client)

    await scorer.score("A novel invention", bundle_all_sources)

    client.complete_single.assert_awaited_once_with(client.complete_single.await_args.args[0], None)


@pytest.mark.asyncio
async def test_single_scorer_forwards_empty_string_ai_model_without_coercion(bundle_all_sources):
    client = _make_client(_VALID_RESPONSE)
    scorer = SingleModelScorer(client)

    await scorer.score("A novel invention", bundle_all_sources, ai_model="")

    client.complete_single.assert_awaited_once_with(client.complete_single.await_args.args[0], "")
