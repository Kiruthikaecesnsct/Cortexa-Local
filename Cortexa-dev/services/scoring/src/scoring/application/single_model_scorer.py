import logging

from scoring.application.parsing.five_axis_parser import parse_five_axes
from scoring.application.prompt.grounded_prompt_builder import (
    HitRenderConfig,
    build_grounded_prompt,
)
from scoring.domain.enums.agreement_level import AgreementLevel
from scoring.domain.errors.scoring_errors import SingleScoringFailedError
from scoring.domain.models.dual_scoring_verdict import DualScoringVerdict
from scoring.domain.models.evidence_bundle import EvidenceBundle
from scoring.infrastructure.clients.model_router_client import ModelRouterClient

logger = logging.getLogger(__name__)


class SingleModelScorer:
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
        ai_model: str | None = None,
    ) -> DualScoringVerdict:
        prompt = build_grounded_prompt(candidate_description, bundle, self._render_config)
        result = await self._client.complete_single(prompt, ai_model)

        if result.error or not result.content:
            raise SingleScoringFailedError(result.error or "empty response from model")

        verdict = parse_five_axes(result.content, bundle)
        return DualScoringVerdict(
            primary=verdict,
            secondary=None,
            agreement_level=AgreementLevel.SingleConfigured,
            agreeing_axis_count=0,
            single_reason="dual_mode_disabled",
        )
