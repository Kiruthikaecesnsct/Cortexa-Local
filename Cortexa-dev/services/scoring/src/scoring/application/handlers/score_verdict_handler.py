import logging
from dataclasses import dataclass

from scoring.application.dtos.score_verdict_request import ScoreVerdictRequestDto
from scoring.application.dtos.store_verdict_response import StoreVerdictResponseDto
from scoring.application.dual_model_scorer import DualModelScorer
from scoring.application.handlers.event_helpers import publish_verdict_event
from scoring.application.prompt.grounded_prompt_builder import HitRenderConfig
from scoring.application.single_model_scorer import SingleModelScorer
from scoring.domain.events.scoring_completed import make_scoring_completed_event
from scoring.domain.models.build_verdict_request import BuildVerdictRequest
from scoring.domain.models.dual_scoring_verdict import DualScoringVerdict
from scoring.domain.models.evidence_bundle import EvidenceBundle
from scoring.domain.models.stored_verdict import StoredVerdict
from scoring.domain.repositories.storage_protocols import EventPublisher, VerdictRepository
from scoring.infrastructure.clients.model_router_client import ModelRouterClient
from scoring.infrastructure.config.settings import ScoringSettings

_logger = logging.getLogger(__name__)


@dataclass
class ScoreVerdictDeps:
    repository: VerdictRepository
    publisher: EventPublisher
    settings: ScoringSettings
    model_router_client: ModelRouterClient


class ScoreVerdictHandler:
    def __init__(self, deps: ScoreVerdictDeps) -> None:
        self._deps = deps

    async def handle(self, request: ScoreVerdictRequestDto) -> StoreVerdictResponseDto:
        dual = await self._score(request.candidate_description, request.bundle, request.ai_model)
        verdict = self._build_stored_verdict(dual, request.bundle)
        await self._deps.repository.save(verdict)
        event = make_scoring_completed_event(verdict, request.correlation_id)
        await publish_verdict_event(
            self._deps.publisher,
            self._deps.settings.scoring_completed_topic,
            verdict,
            event,
        )
        return StoreVerdictResponseDto(
            verdict_id=verdict.id,
            document_id=verdict.document_id,
            published=True,
        )

    async def _score(
        self, candidate_description: str, bundle: EvidenceBundle, ai_model: str | None
    ) -> DualScoringVerdict:
        client = self._deps.model_router_client
        render_config = self._build_render_config()
        if self._deps.settings.dual_mode_enabled:
            _logger.info("dual_mode_enabled=True — invoking DualModelScorer")
            return await DualModelScorer(client, render_config).score(candidate_description, bundle)
        _logger.info("dual_mode_enabled=False — invoking SingleModelScorer")
        return await SingleModelScorer(client, render_config).score(
            candidate_description, bundle, ai_model
        )

    def _build_render_config(self) -> HitRenderConfig:
        settings = self._deps.settings
        return HitRenderConfig(
            max_claim_chars_per_hit=settings.max_claim_chars_per_hit,
            max_abstract_chars_per_hit=settings.max_abstract_chars_per_hit,
            claims_per_hit=settings.claims_per_hit,
        )

    @staticmethod
    def _build_stored_verdict(dual: DualScoringVerdict, bundle: EvidenceBundle) -> StoredVerdict:
        primary = dual.primary
        return StoredVerdict.build(
            BuildVerdictRequest(
                batch_id=primary.batch_id,
                job_id=primary.job_id,
                candidate_id=primary.candidate_id,
                document_id=primary.document_id,
                axes=primary.axes,
                agreement_level=dual.agreement_level,
                agreeing_axis_count=dual.agreeing_axis_count,
                grounding_meets_minimum=bundle.meets_minimum_sources,
                grounding_source_count=bundle.active_source_count,
                single_reason=dual.single_reason,
            )
        )
