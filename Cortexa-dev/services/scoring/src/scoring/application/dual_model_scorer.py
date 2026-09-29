import logging

from scoring.application.parsing.five_axis_parser import parse_five_axes
from scoring.application.prompt.grounded_prompt_builder import (
    HitRenderConfig,
    build_grounded_prompt,
)
from scoring.domain.enums.agreement_level import AgreementLevel
from scoring.domain.errors.scoring_errors import AxisParseError, UngroundedVerdictError
from scoring.domain.models.dual_scoring_verdict import DualScoringVerdict
from scoring.domain.models.evidence_bundle import EvidenceBundle
from scoring.domain.models.scoring_verdict import ScoringVerdict
from scoring.domain.services.agreement_calculator import compute_agreement
from scoring.infrastructure.clients.model_router_client import ModelRouterClient

logger = logging.getLogger(__name__)


def _parse_secondary_safe(content: str, bundle: EvidenceBundle) -> ScoringVerdict | None:
    try:
        return parse_five_axes(content, bundle)
    except (AxisParseError, UngroundedVerdictError) as exc:
        logger.warning("secondary model parse failed, falling back to single: %s", exc)
        return None


def _secondary_failed(content: str, error: str | None) -> bool:
    return error is not None or not content


def _classify_secondary_failure_reason(error: str | None) -> str:
    if error and "timeout" in error.lower():
        return "secondary_timeout"
    return "secondary_unavailable"


class DualModelScorer:
    def __init__(
        self,
        client: ModelRouterClient,
        render_config: HitRenderConfig = HitRenderConfig(),
    ) -> None:
        self._client = client
        self._render_config = render_config

    async def score(
        self,
        candidate_description: str,
        bundle: EvidenceBundle,
    ) -> DualScoringVerdict:
        prompt = build_grounded_prompt(candidate_description, bundle, self._render_config)
        dual = await self._client.complete_dual(prompt)

        primary_verdict = parse_five_axes(dual.primary.content, bundle)

        if _secondary_failed(dual.secondary.content, dual.secondary.error):
            reason = _classify_secondary_failure_reason(dual.secondary.error)
            logger.warning(
                "secondary model unavailable, falling back to single: reason=%s error=%s",
                reason,
                dual.secondary.error,
            )
            return DualScoringVerdict(
                primary=primary_verdict,
                secondary=None,
                agreement_level=AgreementLevel.FallbackSingle,
                agreeing_axis_count=0,
                single_reason=reason,
            )

        secondary_verdict = _parse_secondary_safe(dual.secondary.content, bundle)

        if secondary_verdict is None:
            logger.warning(
                "secondary model parse failed, falling back to single: "
                "reason=secondary_parse_failed"
            )
            return DualScoringVerdict(
                primary=primary_verdict,
                secondary=None,
                agreement_level=AgreementLevel.FallbackSingle,
                agreeing_axis_count=0,
                single_reason="secondary_parse_failed",
            )

        level, count = compute_agreement(primary_verdict, secondary_verdict)
        return DualScoringVerdict(
            primary=primary_verdict,
            secondary=secondary_verdict,
            agreement_level=level,
            agreeing_axis_count=count,
        )
