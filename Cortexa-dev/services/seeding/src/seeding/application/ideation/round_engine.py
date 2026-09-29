import logging
import time
from dataclasses import dataclass, field
from enum import Enum

from seeding.application.parsing.candidate_id_normalizer import normalize_candidate_id
from seeding.application.parsing.ideation_parser import (
    parse_critique,
    parse_propose,
    parse_refine,
)
from seeding.application.prompt.ideation_prompt_budget import (
    fit_critique_prompt,
    fit_propose_prompt,
    fit_refine_prompt,
)
from seeding.application.prompt.ideation_prompt_builder import (
    CritiqueInputs,
    ProposeInputs,
    RefineInputs,
)
from seeding.domain.models.digest import InventionContextBrief
from seeding.domain.models.ideation import (
    IdeaSketch,
    RefinedIdea,
    RetrievedExcerpt,
    RoundRecord,
)
from seeding.domain.models.landscape import ConceptLandscape, PriorArtLandscape
from seeding.domain.models.scratchpad import (
    AcceptedIdea,
    IdeationScratchpad,
    RejectedIdea,
)
from seeding.domain.ports.chunk_read_port import ChunkReadPort
from seeding.domain.ports.model_router_port import ModelRouterPort
from seeding.domain.ports.vector_router_port import VectorRouterPort
from seeding.domain.validation.prompt_injection import neutralize_untrusted
from seeding.infrastructure.config.settings import SeedingSettings
from seeding.infrastructure.cosmos.digest_repository import DigestRepository
from seeding.infrastructure.cosmos.landscape_repository import LandscapeRepository
from seeding.infrastructure.cosmos.scratchpad_repository import (
    ScratchpadRepository,
    scratchpad_id,
)
from seeding.infrastructure.observability.llm_telemetry import LlmCallTags, llm_call_span

logger = logging.getLogger(__name__)

_MAX_ORDER = 2_000_000_000
_ASSET_TARGET = "asset"
_CROWDED_DENSITIES = {"crowded", "dense"}
_SPARSE_DENSITY = "sparse"


class RoundStatus(Enum):
    COMPLETE = "complete"
    EXHAUSTED = "exhausted"
    RESUME_NEEDED = "resume_needed"


class _DeadlineExceeded(Exception):
    pass


@dataclass
class RoundEngineDeps:
    chunk_repo: ChunkReadPort
    digest_repo: DigestRepository
    landscape_repo: LandscapeRepository
    scratchpad_repo: ScratchpadRepository
    vector_client: VectorRouterPort
    client: ModelRouterPort
    settings: SeedingSettings


@dataclass(frozen=True)
class IdeationContext:
    batch_id: str
    document_id: str
    correlation_id: str | None
    ai_model: str | None
    roadmap_context: str | None
    deadline: float


@dataclass
class RoundEngineResult:
    status: RoundStatus
    scratchpad: IdeationScratchpad


@dataclass
class _RoundOutcome:
    accepted: list[AcceptedIdea] = field(default_factory=list)
    rejections: list[RejectedIdea] = field(default_factory=list)
    covered: list[str] = field(default_factory=list)
    record: RoundRecord = field(default_factory=RoundRecord)


def _normalize_concept(text: str) -> str:
    return " ".join(str(text).lower().split())


def _is_sparse(concept: ConceptLandscape) -> bool:
    return concept.density == _SPARSE_DENSITY


def _is_crowded(concept: ConceptLandscape) -> bool:
    return concept.density in _CROWDED_DENSITIES


def _theme_candidates(digest: InventionContextBrief, landscape: PriorArtLandscape) -> list[str]:
    themes = [item.text for item in landscape.whitespace]
    themes += [c.concept for c in landscape.concepts if _is_sparse(c)]
    themes += [entry.text for entry in digest.future_work]
    themes += [entry.text for entry in digest.limitations]
    if digest.problem_space.text.strip():
        themes.append(digest.problem_space.text)
    themes += [entry.text for entry in digest.key_concepts]
    return themes


def build_theme_queries(
    digest: InventionContextBrief,
    landscape: PriorArtLandscape,
    covered: list[str],
    limit: int,
) -> list[str]:
    covered_norm = {_normalize_concept(c) for c in covered}
    result: list[str] = []
    seen: set[str] = set()
    for candidate in _theme_candidates(digest, landscape):
        text = str(candidate).strip()
        if not text:
            continue
        norm = _normalize_concept(text)
        if norm in covered_norm or norm in seen:
            continue
        seen.add(norm)
        result.append(text)
        if len(result) >= limit:
            break
    return result


def _hit_to_excerpt(hit: dict, char_limit: int) -> RetrievedExcerpt:
    payload = hit.get("payload") or {}
    return RetrievedExcerpt(
        chunk_id=str(payload.get("chunk_id") or ""),
        section_label=str(payload.get("section_label") or ""),
        text=str(payload.get("text_excerpt") or "")[:char_limit],
    )


def _crowded_concepts(landscape: PriorArtLandscape) -> list[ConceptLandscape]:
    return [concept for concept in landscape.concepts if _is_crowded(concept)]


class RoundEngine:
    def __init__(self, deps: RoundEngineDeps) -> None:
        self._deps = deps

    def _tags(self, ctx: IdeationContext, stage: str, round_index: int) -> LlmCallTags:
        settings = self._deps.settings
        return LlmCallTags(
            stage=stage,
            task_kind=settings.ideation_task_kind,
            batch_id=ctx.batch_id,
            document_id=ctx.document_id,
            correlation_id=ctx.correlation_id,
            model_requested=ctx.ai_model,
            max_output_tokens=settings.ideation_max_output_tokens,
            round_index=round_index,
        )

    async def run(self, ctx: IdeationContext) -> RoundEngineResult:
        brief = await self._deps.digest_repo.get_brief(
            ctx.batch_id, ctx.document_id, self._deps.settings.digest_prompt_version
        )
        scratchpad = await self._load_scratchpad(ctx)
        if brief is None:
            return RoundEngineResult(RoundStatus.RESUME_NEEDED, scratchpad)
        known_ids = await self._known_chunk_ids(ctx)
        if brief.is_empty or not known_ids:
            return await self._terminalize(scratchpad)
        landscape = await self._load_landscape(ctx)
        return await self._run_rounds(ctx, scratchpad, brief, landscape, known_ids)

    async def _load_scratchpad(self, ctx: IdeationContext) -> IdeationScratchpad:
        settings = self._deps.settings
        existing = await self._deps.scratchpad_repo.get(
            ctx.batch_id, ctx.document_id, settings.ideation_schema_version
        )
        if existing is not None:
            return existing
        return IdeationScratchpad(
            id=scratchpad_id(ctx.batch_id, ctx.document_id, settings.ideation_schema_version),
            batch_id=ctx.batch_id,
            document_id=ctx.document_id,
            schema_version=settings.ideation_schema_version,
            prompt_version=settings.ideation_prompt_version,
        )

    async def _known_chunk_ids(self, ctx: IdeationContext) -> set[str]:
        chunks = await self._deps.chunk_repo.get_range(ctx.batch_id, ctx.document_id, 0, _MAX_ORDER)
        return {str(chunk["id"]) for chunk in chunks if chunk.get("id")}

    async def _load_landscape(self, ctx: IdeationContext) -> PriorArtLandscape:
        settings = self._deps.settings
        landscape = await self._deps.landscape_repo.get_landscape(
            ctx.batch_id, ctx.document_id, settings.landscape_schema_version
        )
        if landscape is not None:
            return landscape
        return PriorArtLandscape(
            id="",
            batch_id=ctx.batch_id,
            document_id=ctx.document_id,
            schema_version=settings.landscape_schema_version,
        )

    async def _run_rounds(
        self,
        ctx: IdeationContext,
        scratchpad: IdeationScratchpad,
        brief: InventionContextBrief,
        landscape: PriorArtLandscape,
        known_ids: set[str],
    ) -> RoundEngineResult:
        max_rounds = self._deps.settings.ideation_max_rounds
        while scratchpad.rounds_completed < max_rounds:
            if time.monotonic() >= ctx.deadline:
                return await self._suspend(ctx, scratchpad)
            try:
                outcome = await self._run_round(ctx, scratchpad, brief, landscape, known_ids)
            except _DeadlineExceeded:
                return await self._suspend(ctx, scratchpad)
            self._apply(scratchpad, outcome)
            await self._deps.scratchpad_repo.save(scratchpad)
            if outcome.record.accepted_count == 0:
                break
        return await self._terminalize(scratchpad)

    async def _suspend(
        self, ctx: IdeationContext, scratchpad: IdeationScratchpad
    ) -> RoundEngineResult:
        await self._deps.scratchpad_repo.save(scratchpad)
        logger.info(
            "batch_id=%s document_id=%s round=%d outcome=TRANSIENT reason=time_budget",
            ctx.batch_id,
            ctx.document_id,
            scratchpad.rounds_completed,
        )
        return RoundEngineResult(RoundStatus.RESUME_NEEDED, scratchpad)

    def _apply(self, scratchpad: IdeationScratchpad, outcome: _RoundOutcome) -> None:
        scratchpad.accepted_ideas.extend(outcome.accepted)
        scratchpad.rejections.extend(outcome.rejections)
        scratchpad.covered_concepts.extend(outcome.covered)
        scratchpad.rounds.append(outcome.record)
        scratchpad.rounds_completed += 1

    async def _terminalize(self, scratchpad: IdeationScratchpad) -> RoundEngineResult:
        if scratchpad.accepted_ideas:
            scratchpad.status = "complete"
            status = RoundStatus.COMPLETE
        else:
            scratchpad.status = "exhausted"
            status = RoundStatus.EXHAUSTED
        await self._deps.scratchpad_repo.save(scratchpad)
        return RoundEngineResult(status, scratchpad)

    def _ensure_deadline(self, ctx: IdeationContext) -> None:
        if time.monotonic() >= ctx.deadline:
            raise _DeadlineExceeded

    async def _retrieve(
        self,
        ctx: IdeationContext,
        digest: InventionContextBrief,
        landscape: PriorArtLandscape,
        scratchpad: IdeationScratchpad,
    ) -> list[RetrievedExcerpt]:
        settings = self._deps.settings
        themes = build_theme_queries(
            digest, landscape, scratchpad.covered_concepts, settings.ideation_themes_per_round
        )
        if not themes:
            return []
        vectors = await self._deps.vector_client.embed(themes)
        excerpts: dict[str, RetrievedExcerpt] = {}
        for vector in vectors:
            hits = await self._deps.vector_client.search(
                vector,
                settings.ideation_retrieval_top_k,
                _ASSET_TARGET,
                [{"field": "batch_id", "value": ctx.batch_id}],
            )
            for hit in hits:
                excerpt = _hit_to_excerpt(hit, settings.ideation_excerpt_char_limit)
                if excerpt.chunk_id and excerpt.chunk_id not in excerpts:
                    excerpts[excerpt.chunk_id] = excerpt
        return list(excerpts.values())

    async def _run_round(
        self,
        ctx: IdeationContext,
        scratchpad: IdeationScratchpad,
        digest: InventionContextBrief,
        landscape: PriorArtLandscape,
        known_ids: set[str],
    ) -> _RoundOutcome:
        self._ensure_deadline(ctx)
        excerpts = await self._retrieve(ctx, digest, landscape, scratchpad)
        sketches = await self._propose(ctx, digest, landscape, scratchpad, excerpts)
        record = self._new_record(scratchpad.rounds_completed, allowed_themes=excerpts)
        record.proposed_count = len(sketches)
        if not sketches:
            return _RoundOutcome(record=record)
        self._ensure_deadline(ctx)
        critique = await self._critique(ctx, sketches, digest, landscape, scratchpad)
        outcome = self._build_reject_outcome(sketches, critique, record)
        accepted_sketches = [s for s in sketches if s.sketch_id in critique.accepted_ids]
        if not accepted_sketches:
            return outcome
        self._ensure_deadline(ctx)
        refined = await self._refine(ctx, accepted_sketches, excerpts, scratchpad.rounds_completed)
        self._collect_accepted(outcome, refined, accepted_sketches, excerpts, scratchpad)
        return outcome

    def _new_record(self, round_index: int, allowed_themes: list[RetrievedExcerpt]) -> RoundRecord:
        return RoundRecord(round=round_index, themes=[e.section_label for e in allowed_themes])

    async def _propose(
        self,
        ctx: IdeationContext,
        digest: InventionContextBrief,
        landscape: PriorArtLandscape,
        scratchpad: IdeationScratchpad,
        excerpts: list[RetrievedExcerpt],
    ) -> list[IdeaSketch]:
        settings = self._deps.settings
        inputs = ProposeInputs(
            ideas_per_round=settings.ideation_ideas_per_round,
            digest=digest,
            landscape=landscape,
            roadmap_context=ctx.roadmap_context,
            excerpts=excerpts,
            scratchpad=scratchpad,
            roadmap_char_limit=settings.ideation_roadmap_char_limit,
            excerpt_char_limit=settings.ideation_excerpt_char_limit,
            summary_char_limit=settings.ideation_scratchpad_summary_char_limit,
        )
        prompt, shown = fit_propose_prompt(inputs, settings.ideation_propose_max_input_tokens)
        allowed = {normalize_candidate_id(cid) for cid in shown}
        tags = self._tags(ctx, "ideation_propose", scratchpad.rounds_completed)
        async with llm_call_span(logger, tags) as span:
            result = await self._deps.client.complete(prompt, [], model=ctx.ai_model)
            span.result = result
        parsed = parse_propose(result.content, allowed, settings.ideation_ideas_per_round)
        self._log_drops(ctx, "propose", parsed.dropped)
        return parsed.sketches

    async def _critique(
        self,
        ctx: IdeationContext,
        sketches: list[IdeaSketch],
        digest: InventionContextBrief,
        landscape: PriorArtLandscape,
        scratchpad: IdeationScratchpad,
    ):
        inputs = CritiqueInputs(
            sketches=sketches,
            problem_space=digest.problem_space,
            contributions=digest.contributions,
            accepted=scratchpad.accepted_ideas,
            crowded=_crowded_concepts(landscape),
        )
        prompt = fit_critique_prompt(inputs, self._deps.settings.ideation_critique_max_input_tokens)
        tags = self._tags(ctx, "ideation_critique", scratchpad.rounds_completed)
        async with llm_call_span(logger, tags) as span:
            result = await self._deps.client.complete(prompt, [], model=ctx.ai_model)
            span.result = result
        parsed = parse_critique(result.content, {s.sketch_id for s in sketches})
        self._log_drops(ctx, "critique", parsed.dropped)
        return parsed

    async def _refine(
        self,
        ctx: IdeationContext,
        accepted_sketches: list[IdeaSketch],
        excerpts: list[RetrievedExcerpt],
        round_index: int,
    ) -> list[RefinedIdea]:
        settings = self._deps.settings
        cited_ids = {cid for sketch in accepted_sketches for cid in sketch.chunk_ids}
        cited = [excerpt for excerpt in excerpts if excerpt.chunk_id in cited_ids]
        inputs = RefineInputs(
            sketches=accepted_sketches,
            excerpts=cited,
            roadmap_context=ctx.roadmap_context,
            roadmap_char_limit=settings.ideation_roadmap_char_limit,
            excerpt_char_limit=settings.ideation_excerpt_char_limit,
        )
        prompt = fit_refine_prompt(inputs, settings.ideation_refine_max_input_tokens)
        sketch_map = {sketch.sketch_id: sketch for sketch in accepted_sketches}
        roadmap_supplied = bool(ctx.roadmap_context and ctx.roadmap_context.strip())
        tags = self._tags(ctx, "ideation_refine", round_index)
        async with llm_call_span(logger, tags) as span:
            result = await self._deps.client.complete(prompt, [], model=ctx.ai_model)
            span.result = result
        parsed = parse_refine(result.content, sketch_map, roadmap_supplied)
        self._log_drops(ctx, "refine", parsed.dropped)
        return parsed.opportunities

    def _build_reject_outcome(self, sketches, critique, record: RoundRecord) -> _RoundOutcome:
        outcome = _RoundOutcome(record=record)
        by_id = {s.sketch_id: s for s in sketches}
        for sketch_id, reason in critique.rejections:
            sketch = by_id.get(sketch_id)
            summary = sketch.title if sketch else sketch_id
            outcome.rejections.append(RejectedIdea(summary=summary, reason_code=reason))
            record.rejected_reasons.append(reason)
        return outcome

    def _collect_accepted(
        self,
        outcome: _RoundOutcome,
        refined: list[RefinedIdea],
        accepted_sketches: list[IdeaSketch],
        excerpts: list[RetrievedExcerpt],
        scratchpad: IdeationScratchpad,
    ) -> None:
        by_chunk = {excerpt.chunk_id: excerpt for excerpt in excerpts}
        concept_by_id = {s.sketch_id: s.target_concept for s in accepted_sketches}
        for idea in refined:
            concept = concept_by_id.get(idea.sketch_id, "")
            outcome.accepted.append(
                _to_accepted_idea(idea, concept, scratchpad.rounds_completed, by_chunk)
            )
            if concept.strip():
                outcome.covered.append(concept)
        outcome.record.accepted_count = len(refined)

    def _log_drops(self, ctx: IdeationContext, stage: str, dropped: list[str]) -> None:
        if not dropped:
            return
        logger.info(
            "batch_id=%s document_id=%s stage=%s dropped=%d",
            ctx.batch_id,
            ctx.document_id,
            stage,
            len(dropped),
        )


def _to_accepted_idea(
    idea: RefinedIdea,
    target_concept: str,
    round_index: int,
    by_chunk: dict[str, RetrievedExcerpt],
) -> AcceptedIdea:
    grounding = [by_chunk[cid] for cid in idea.chunk_ids if cid in by_chunk]
    return AcceptedIdea(
        title=neutralize_untrusted(idea.title),
        summary=neutralize_untrusted(idea.description),
        novelty_delta=neutralize_untrusted(idea.novelty_delta),
        chunk_ids=idea.chunk_ids,
        description=neutralize_untrusted(idea.description),
        mechanism=neutralize_untrusted(idea.mechanism),
        claim_statement=neutralize_untrusted(idea.claim_statement),
        category=str(idea.category.value),
        roadmap_alignment=neutralize_untrusted(idea.roadmap_alignment),
        target_concept=target_concept,
        round_index=round_index,
        excerpts=grounding,
    )
