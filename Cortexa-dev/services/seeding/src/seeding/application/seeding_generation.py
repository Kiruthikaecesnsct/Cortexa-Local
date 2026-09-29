import logging
import re
from dataclasses import dataclass
from uuid import uuid4

from seeding.application.parsing.json_extractor import extract_json
from seeding.application.parsing.seeding_opportunity_parser import parse_seeding_opportunities
from seeding.application.prompt.seeding_opportunity_prompt_builder import (
    build_seeding_prompt,
    sanitize_candidate_id,
)
from seeding.domain.errors.seeding_errors import (
    OpportunityParseError,
    UngroundedSeedingError,
)
from seeding.domain.models.seeding_result import SeedingOpportunity, SeedingResult
from seeding.domain.ports.model_router_port import ModelRouterPort
from seeding.infrastructure.config.settings import SeedingSettings
from seeding.infrastructure.observability.llm_telemetry import LlmCallTags, llm_call_span

_logger = logging.getLogger(__name__)


def _build_result(candidates: list[dict], opportunities: list[SeedingOpportunity]) -> SeedingResult:
    return SeedingResult(
        id=str(uuid4()),
        batch_id=candidates[0]["batch_id"],
        document_id=str(candidates[0].get("document_id", "")),
        engine="seeding",
        opportunities=opportunities,
    )


def _chunk(items: list, size: int) -> list[list]:
    return [items[i : i + size] for i in range(0, len(items), size)]


def _normalize_title(title: str) -> str:
    stripped = title.strip()
    collapsed = re.sub(r"\s+", " ", stripped)
    cleaned = collapsed.strip(".,;:!?\"'")
    return cleaned.lower()


def _dedupe(opportunities: list[SeedingOpportunity]) -> list[SeedingOpportunity]:
    seen: set[str] = set()
    result = []
    for opp in opportunities:
        normalized = _normalize_title(opp.title)
        if normalized and normalized in seen:
            continue
        if normalized:
            seen.add(normalized)
        result.append(opp)
    return result


@dataclass
class _ChunkGenerationContext:
    roadmap_context: str | None
    batch_candidate_ids: set[str]
    client: ModelRouterPort
    ai_model: str | None
    batch_id: str
    document_id: str
    task_kind: str
    max_output_tokens: int


def _report_tags(ctx: _ChunkGenerationContext) -> LlmCallTags:
    return LlmCallTags(
        stage="report",
        task_kind=ctx.task_kind,
        batch_id=ctx.batch_id,
        document_id=ctx.document_id,
        correlation_id=None,
        model_requested=ctx.ai_model,
        max_output_tokens=ctx.max_output_tokens,
    )


async def _generate_for_chunk(
    ctx: _ChunkGenerationContext,
    chunk: list[dict],
    chunk_index: int,
) -> list[SeedingOpportunity]:
    prompt, chunk_ids = build_seeding_prompt(chunk, ctx.roadmap_context)
    async with llm_call_span(_logger, _report_tags(ctx)) as span:
        result = await ctx.client.complete(prompt, chunk_ids, model=ctx.ai_model)
        span.result = result
    try:
        raw = extract_json(result.content)
        parsed = parse_seeding_opportunities(raw, ctx.batch_candidate_ids)
        _logger.info(
            "Chunk %d: requested=%d parsed=%d dropped=%d%s",
            chunk_index,
            len(chunk),
            len(parsed.opportunities),
            len(parsed.dropped),
            f" drop_reasons=[{'; '.join(parsed.dropped)}]" if parsed.dropped else "",
        )
        return parsed.opportunities
    except (OpportunityParseError, UngroundedSeedingError) as e:
        _logger.warning("Chunk %d parse failed: %s", chunk_index, str(e))
        return []


async def generate_seeding_opportunities(
    candidates: list[dict],
    roadmap_context: str | None,
    client: ModelRouterPort,
    settings: SeedingSettings,
    ai_model: str | None = None,
) -> SeedingResult:
    if not candidates:
        raise UngroundedSeedingError("no candidates available for seeding generation")

    ctx = _ChunkGenerationContext(
        roadmap_context=roadmap_context,
        batch_candidate_ids={sanitize_candidate_id(c) for c in candidates},
        client=client,
        ai_model=ai_model,
        batch_id=str(candidates[0]["batch_id"]),
        document_id=str(candidates[0].get("document_id", "")),
        task_kind=settings.seeding_task_kind,
        max_output_tokens=settings.seeding_max_output_tokens,
    )

    chunks = _chunk(candidates, settings.seeding_candidates_per_call)
    all_opportunities = []
    total_requested = 0
    total_parsed = 0

    for idx, chunk in enumerate(chunks):
        total_requested += len(chunk)
        chunk_opps = await _generate_for_chunk(ctx, chunk, idx)
        total_parsed += len(chunk_opps)
        all_opportunities.extend(chunk_opps)

    merged = _dedupe(all_opportunities)
    total_dropped = total_requested - total_parsed

    _logger.info(
        "Batch summary: requested=%d parsed=%d dropped=%d dedupe_result=%d",
        total_requested,
        total_parsed,
        total_dropped,
        len(merged),
    )

    if not merged:
        raise OpportunityParseError("no opportunities from any chunk")

    return _build_result(candidates, merged)
