from dataclasses import dataclass, field
from uuid import uuid4

from seeding.application.ideation.round_engine import (
    IdeationContext,
    RoundEngine,
    RoundStatus,
)
from seeding.domain.enums.opportunity_category import OpportunityCategory
from seeding.domain.models.ideation import Grounding
from seeding.domain.models.scratchpad import AcceptedIdea, IdeationScratchpad
from seeding.domain.models.seeding_result import SeedingOpportunity, SeedingResult

_CATEGORY_VALUES = {category.value for category in OpportunityCategory}


@dataclass
class DeepGenerationOutcome:
    result: SeedingResult | None
    resume_needed: bool
    accepted_ideas: list[AcceptedIdea] = field(default_factory=list)


def _category(raw: str) -> OpportunityCategory | None:
    return OpportunityCategory(raw) if raw in _CATEGORY_VALUES else None


def _to_opportunity(idea: AcceptedIdea) -> SeedingOpportunity:
    grounding = Grounding(chunk_ids=idea.chunk_ids, excerpts=idea.excerpts)
    return SeedingOpportunity(
        id=str(uuid4()),
        title=idea.title,
        description=idea.description or idea.summary,
        confidence_score=0.0,
        roadmap_alignment=idea.roadmap_alignment,
        category=_category(idea.category),
        innovation_rationale=idea.novelty_delta,
        source_candidate_ids=[],
        grounded_in=grounding,
        novelty_delta=idea.novelty_delta,
        mechanism=idea.mechanism,
        claim_statement=idea.claim_statement,
        round_index=idea.round_index,
    )


def _empty_explanation(scratchpad: IdeationScratchpad) -> str:
    return (
        "ideation produced no novel additions beyond the document over "
        f"{scratchpad.rounds_completed} rounds"
    )


def _build_result(scratchpad: IdeationScratchpad, ctx: IdeationContext) -> SeedingResult:
    opportunities = [_to_opportunity(idea) for idea in scratchpad.accepted_ideas]
    is_empty = not opportunities
    explanation = _empty_explanation(scratchpad) if is_empty else ""
    return SeedingResult(
        id=str(uuid4()),
        batch_id=ctx.batch_id,
        document_id=ctx.document_id,
        engine="seeding",
        opportunities=opportunities,
        rounds=scratchpad.rounds,
        scratchpad_id=scratchpad.id,
        explanation=explanation,
        is_empty=is_empty,
    )


async def generate_deep_seeding(engine: RoundEngine, ctx: IdeationContext) -> DeepGenerationOutcome:
    engine_result = await engine.run(ctx)
    if engine_result.status == RoundStatus.RESUME_NEEDED:
        return DeepGenerationOutcome(result=None, resume_needed=True)
    scratchpad = engine_result.scratchpad
    result = _build_result(scratchpad, ctx)
    return DeepGenerationOutcome(
        result=result,
        resume_needed=False,
        accepted_ideas=list(scratchpad.accepted_ideas),
    )
