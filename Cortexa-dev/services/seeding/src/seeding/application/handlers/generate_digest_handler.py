import logging
import math
import time
from dataclasses import dataclass
from uuid import uuid5

from pydantic import ValidationError

from seeding.application.handlers.process_seeding_request_handler import ProcessOutcome
from seeding.application.parsing.digest_parser import (
    MapNote,
    ReduceBrief,
    parse_map_note,
    parse_reduce_brief,
)
from seeding.application.prompt.digest_prompt_builder import build_map_prompt, build_reduce_prompt
from seeding.domain.errors.seeding_errors import (
    DigestParseError,
    ModelRouterFailedError,
)
from seeding.domain.events.digest import DigestEnvelope, make_digest_completed_event
from seeding.domain.events.seeding_failed import make_seeding_failed_event
from seeding.domain.models.digest import (
    CitedEntry,
    DigestReduceIntermediate,
    InventionContextBrief,
    SectionNote,
)
from seeding.domain.ports.chunk_read_port import ChunkReadPort
from seeding.domain.ports.event_publisher_port import EventPublisherPort
from seeding.domain.ports.model_router_port import ModelRouterPort
from seeding.domain.services.chunk_grouping import (
    DIGEST_NS,
    ChunkGroup,
    group_chunks,
    section_key,
)
from seeding.infrastructure.config.settings import SeedingSettings
from seeding.infrastructure.cosmos.digest_repository import DigestRepository
from seeding.infrastructure.observability.llm_telemetry import LlmCallTags, llm_call_span

logger = logging.getLogger(__name__)

_PERMANENT_ERRORS = (DigestParseError, ValidationError)
_TRIGGER_TYPE = "pipeline"
_MAX_ORDER = 2_000_000_000
_CHARS_PER_TOKEN = 4
_MAP_FIELDS = ("claims_made", "methods_used", "limitations", "future_work", "key_concepts")


@dataclass
class GenerateDigestDeps:
    chunk_repo: ChunkReadPort
    digest_repo: DigestRepository
    client: ModelRouterPort
    publisher: EventPublisherPort
    settings: SeedingSettings


@dataclass(frozen=True)
class _DigestContext:
    batch_id: str
    document_id: str
    correlation_id: str | None
    ai_model: str | None
    deadline: float


def _brief_id(batch_id: str, document_id: str, prompt_version: str) -> str:
    return str(uuid5(DIGEST_NS, f"{batch_id}:{document_id}:{prompt_version}:brief"))


def _entries_to_dicts(entries: list[CitedEntry]) -> list[dict]:
    return [entry.model_dump() for entry in entries]


def _note_to_reduce_input(note: SectionNote) -> dict:
    return {field: _entries_to_dicts(getattr(note, field)) for field in _MAP_FIELDS}


def _brief_to_reduce_input(brief: ReduceBrief) -> dict:
    problem = [brief.problem_space.model_dump()] if brief.problem_space.chunk_ids else []
    return {
        "problem_space": problem,
        "contributions": _entries_to_dicts(brief.contributions),
        "limitations": _entries_to_dicts(brief.limitations),
        "future_work": _entries_to_dicts(brief.future_work),
        "key_concepts": _entries_to_dicts(brief.key_concepts),
        "tech_fields": _entries_to_dicts(brief.tech_fields),
    }


def _note_chunk_ids(note: dict) -> set[str]:
    ids: set[str] = set()
    for entries in note.values():
        for entry in entries:
            ids.update(str(cid) for cid in entry.get("chunk_ids") or [])
    return ids


def _allowed_ids(inputs: list[dict]) -> set[str]:
    allowed: set[str] = set()
    for note in inputs:
        allowed |= _note_chunk_ids(note)
    return allowed


def _estimate_note_tokens(note: dict) -> int:
    chars = 0
    for entries in note.values():
        for entry in entries:
            chars += len(str(entry.get("text") or ""))
            chars += sum(len(str(cid)) for cid in entry.get("chunk_ids") or [])
    return math.ceil(chars / _CHARS_PER_TOKEN)


def _rebalance(inputs: list[dict], max_fan: int) -> list[list[dict]]:
    size = max(1, math.ceil(len(inputs) / max_fan))
    return [inputs[start : start + size] for start in range(0, len(inputs), size)]


def _partition_by_tokens(inputs: list[dict], max_tokens: int, max_fan: int) -> list[list[dict]]:
    batches: list[list[dict]] = []
    current: list[dict] = []
    total = 0
    for note in inputs:
        tokens = _estimate_note_tokens(note)
        if current and total + tokens > max_tokens:
            batches.append(current)
            current, total = [], 0
        current.append(note)
        total += tokens
    if current:
        batches.append(current)
    if len(batches) > max_fan:
        return _rebalance(inputs, max_fan)
    return batches


def _section_note_empty(note: SectionNote) -> bool:
    return not any(getattr(note, field) for field in _MAP_FIELDS)


def _reduce_key(depth: int, part_index: int) -> str:
    return f"reduce:d{depth}#part{part_index}"


def _intermediate_id(batch_id: str, document_id: str, prompt_version: str, reduce_key: str) -> str:
    return str(uuid5(DIGEST_NS, f"{batch_id}:{document_id}:{prompt_version}:{reduce_key}"))


def _intermediate_to_brief(item: DigestReduceIntermediate) -> ReduceBrief:
    return ReduceBrief(
        problem_space=item.problem_space,
        contributions=item.contributions,
        limitations=item.limitations,
        future_work=item.future_work,
        key_concepts=item.key_concepts,
        tech_fields=item.tech_fields,
    )


class GenerateDigestHandler:
    def __init__(self, deps: GenerateDigestDeps) -> None:
        self._deps = deps

    def _tags(self, ctx: _DigestContext) -> LlmCallTags:
        settings = self._deps.settings
        return LlmCallTags(
            stage="digest",
            task_kind=settings.digest_task_kind,
            batch_id=ctx.batch_id,
            document_id=ctx.document_id,
            correlation_id=ctx.correlation_id,
            model_requested=ctx.ai_model,
            max_output_tokens=settings.digest_max_output_tokens,
        )

    async def handle(self, envelope: DigestEnvelope) -> ProcessOutcome:
        try:
            return await self._execute(envelope)
        except Exception as exc:
            return await self._classify_error(exc, envelope)

    def _build_context(self, envelope: DigestEnvelope) -> _DigestContext:
        document_id = envelope.payload.document_id or envelope.document_id or ""
        budget = self._deps.settings.digest_message_time_budget_seconds
        return _DigestContext(
            batch_id=envelope.batch_id,
            document_id=document_id,
            correlation_id=envelope.correlation_id,
            ai_model=envelope.payload.ai_model,
            deadline=time.monotonic() + budget,
        )

    async def _execute(self, envelope: DigestEnvelope) -> ProcessOutcome:
        ctx = self._build_context(envelope)
        prompt_version = self._deps.settings.digest_prompt_version
        existing = await self._deps.digest_repo.get_brief(
            ctx.batch_id, ctx.document_id, prompt_version
        )
        if existing is not None:
            return await self._republish(ctx, existing)
        chunks = await self._deps.chunk_repo.get_range(ctx.batch_id, ctx.document_id, 0, _MAX_ORDER)
        if not chunks:
            return await self._finish_empty(ctx, section_count=0)
        groups = group_chunks(chunks, self._deps.settings.digest_map_max_group_tokens)
        notes = await self._map_all(groups, ctx)
        if notes is None:
            return ProcessOutcome.TRANSIENT
        if all(_section_note_empty(note) for note in notes):
            return await self._finish_empty(ctx, section_count=len(notes))
        data = await self._reduce(notes, ctx)
        if data is None:
            return ProcessOutcome.TRANSIENT
        brief = self._build_brief(ctx, data)
        await self._deps.digest_repo.save_brief(brief)
        return await self._publish_completed(ctx, brief, section_count=len(notes))

    async def _map_all(
        self, groups: list[ChunkGroup], ctx: _DigestContext
    ) -> list[SectionNote] | None:
        prompt_version = self._deps.settings.digest_prompt_version
        existing = await self._deps.digest_repo.get_section_notes(
            ctx.batch_id, ctx.document_id, prompt_version
        )
        by_key = {note.section_key: note for note in existing}
        notes: list[SectionNote] = []
        for group in groups:
            key = section_key(ctx.batch_id, ctx.document_id, group.partition_key)
            note = by_key.get(key)
            if note is None:
                if time.monotonic() >= ctx.deadline:
                    logger.info(
                        "batch_id=%s document_id=%s outcome=TRANSIENT reason=time_budget",
                        ctx.batch_id,
                        ctx.document_id,
                    )
                    return None
                note = await self._map_one(group, key, ctx)
            notes.append(note)
        return notes

    async def _map_one(self, group: ChunkGroup, key: str, ctx: _DigestContext) -> SectionNote:
        prompt = build_map_prompt(group)
        async with llm_call_span(logger, self._tags(ctx)) as span:
            result = await self._deps.client.complete(prompt, [], model=ctx.ai_model)
            span.result = result
        parsed = parse_map_note(result.content, set(group.chunk_ids))
        note = self._build_note(group, key, ctx, parsed)
        await self._deps.digest_repo.save_section_note(note)
        return note

    def _build_note(
        self, group: ChunkGroup, key: str, ctx: _DigestContext, parsed: MapNote
    ) -> SectionNote:
        return SectionNote(
            id=key,
            batch_id=ctx.batch_id,
            document_id=ctx.document_id,
            section_key=key,
            section_label=group.section_label,
            chunk_ids=group.chunk_ids,
            claims_made=parsed.claims_made,
            methods_used=parsed.methods_used,
            limitations=parsed.limitations,
            future_work=parsed.future_work,
            key_concepts=parsed.key_concepts,
            schema_version=self._deps.settings.digest_schema_version,
            prompt_version=self._deps.settings.digest_prompt_version,
        )

    async def _reduce(self, notes: list[SectionNote], ctx: _DigestContext) -> ReduceBrief | None:
        inputs = [_note_to_reduce_input(note) for note in notes]
        return await self._reduce_level(inputs, ctx, depth=0)

    async def _reduce_level(
        self, inputs: list[dict], ctx: _DigestContext, depth: int
    ) -> ReduceBrief | None:
        settings = self._deps.settings
        total = sum(_estimate_note_tokens(note) for note in inputs)
        over_budget = total > settings.digest_reduce_max_input_tokens
        if not over_budget or depth >= settings.digest_reduce_max_depth:
            return await self._final_reduce(inputs, ctx)
        batches = _partition_by_tokens(
            inputs, settings.digest_reduce_max_input_tokens, settings.digest_reduce_max_fan
        )
        if len(batches) <= 1:
            return await self._final_reduce(inputs, ctx)
        intermediates = await self._reduce_intermediates(batches, ctx, depth)
        if intermediates is None:
            return None
        next_inputs = [_brief_to_reduce_input(item) for item in intermediates]
        return await self._reduce_level(next_inputs, ctx, depth + 1)

    async def _final_reduce(self, inputs: list[dict], ctx: _DigestContext) -> ReduceBrief | None:
        if time.monotonic() >= ctx.deadline:
            self._log_reduce_budget(ctx)
            return None
        return await self._reduce_once(inputs, ctx)

    async def _reduce_intermediates(
        self, batches: list[list[dict]], ctx: _DigestContext, depth: int
    ) -> list[ReduceBrief] | None:
        cached = await self._load_intermediates(ctx)
        results: list[ReduceBrief] = []
        for part_index, batch in enumerate(batches):
            existing = cached.get(_reduce_key(depth, part_index))
            if existing is not None:
                results.append(existing)
                continue
            if time.monotonic() >= ctx.deadline:
                self._log_reduce_budget(ctx)
                return None
            data = await self._reduce_once(batch, ctx)
            await self._save_intermediate(ctx, depth, part_index, data)
            results.append(data)
        return results

    async def _load_intermediates(self, ctx: _DigestContext) -> dict[str, ReduceBrief]:
        items = await self._deps.digest_repo.get_intermediates(
            ctx.batch_id, ctx.document_id, self._deps.settings.digest_prompt_version
        )
        return {item.reduce_key: _intermediate_to_brief(item) for item in items}

    async def _save_intermediate(
        self, ctx: _DigestContext, depth: int, part_index: int, data: ReduceBrief
    ) -> None:
        await self._deps.digest_repo.save_intermediate(
            self._build_intermediate(ctx, depth, part_index, data)
        )

    def _build_intermediate(
        self, ctx: _DigestContext, depth: int, part_index: int, data: ReduceBrief
    ) -> DigestReduceIntermediate:
        settings = self._deps.settings
        reduce_key = _reduce_key(depth, part_index)
        return DigestReduceIntermediate(
            id=_intermediate_id(
                ctx.batch_id, ctx.document_id, settings.digest_prompt_version, reduce_key
            ),
            batch_id=ctx.batch_id,
            document_id=ctx.document_id,
            reduce_key=reduce_key,
            depth=depth,
            part_index=part_index,
            is_empty=data.is_empty(),
            problem_space=data.problem_space,
            contributions=data.contributions,
            limitations=data.limitations,
            future_work=data.future_work,
            key_concepts=data.key_concepts,
            tech_fields=data.tech_fields,
            schema_version=settings.digest_schema_version,
            prompt_version=settings.digest_prompt_version,
        )

    def _log_reduce_budget(self, ctx: _DigestContext) -> None:
        logger.info(
            "batch_id=%s document_id=%s outcome=TRANSIENT reason=reduce_time_budget",
            ctx.batch_id,
            ctx.document_id,
        )

    async def _reduce_once(self, inputs: list[dict], ctx: _DigestContext) -> ReduceBrief:
        prompt = build_reduce_prompt(inputs)
        async with llm_call_span(logger, self._tags(ctx)) as span:
            result = await self._deps.client.complete(prompt, [], model=ctx.ai_model)
            span.result = result
        return parse_reduce_brief(result.content, _allowed_ids(inputs))

    def _build_brief(
        self, ctx: _DigestContext, data: ReduceBrief, is_empty: bool = False
    ) -> InventionContextBrief:
        settings = self._deps.settings
        brief_id = _brief_id(ctx.batch_id, ctx.document_id, settings.digest_prompt_version)
        return InventionContextBrief(
            id=brief_id,
            batch_id=ctx.batch_id,
            document_id=ctx.document_id,
            is_empty=is_empty or data.is_empty(),
            problem_space=data.problem_space,
            contributions=data.contributions,
            limitations=data.limitations,
            future_work=data.future_work,
            key_concepts=data.key_concepts,
            tech_fields=data.tech_fields,
            schema_version=settings.digest_schema_version,
            prompt_version=settings.digest_prompt_version,
        )

    async def _finish_empty(self, ctx: _DigestContext, section_count: int) -> ProcessOutcome:
        brief = self._build_brief(ctx, ReduceBrief(), is_empty=True)
        await self._deps.digest_repo.save_brief(brief)
        return await self._publish_completed(ctx, brief, section_count)

    async def _republish(self, ctx: _DigestContext, brief: InventionContextBrief) -> ProcessOutcome:
        notes = await self._deps.digest_repo.get_section_notes(
            ctx.batch_id, ctx.document_id, self._deps.settings.digest_prompt_version
        )
        logger.info(
            "batch_id=%s document_id=%s brief_id=%s outcome=SUCCESS reason=idempotent",
            ctx.batch_id,
            ctx.document_id,
            brief.id,
        )
        return await self._publish_completed(ctx, brief, len(notes))

    async def _publish_completed(
        self, ctx: _DigestContext, brief: InventionContextBrief, section_count: int
    ) -> ProcessOutcome:
        event = make_digest_completed_event(
            batch_id=ctx.batch_id,
            document_id=ctx.document_id,
            empty=brief.is_empty,
            section_count=section_count,
            brief_id=brief.id,
            correlation_id=ctx.correlation_id,
        )
        await self._deps.publisher.publish(
            self._deps.settings.digest_completed_topic,
            event,
            session_id=ctx.batch_id,
            correlation_id=ctx.correlation_id,
        )
        logger.info(
            "batch_id=%s document_id=%s brief_id=%s empty=%s section_count=%d outcome=SUCCESS",
            ctx.batch_id,
            ctx.document_id,
            brief.id,
            brief.is_empty,
            section_count,
        )
        return ProcessOutcome.SUCCESS

    async def _classify_error(self, exc: Exception, envelope: DigestEnvelope) -> ProcessOutcome:
        batch_id = envelope.batch_id
        document_id = envelope.payload.document_id or envelope.document_id
        correlation_id = envelope.correlation_id
        if isinstance(exc, ModelRouterFailedError):
            outcome = self._classify_model_error(exc, batch_id, correlation_id)
        else:
            outcome = (
                ProcessOutcome.PERMANENT
                if isinstance(exc, _PERMANENT_ERRORS)
                else ProcessOutcome.TRANSIENT
            )
            logger.error(
                "batch_id=%s correlation_id=%s outcome=%s error_class=%s",
                batch_id,
                correlation_id,
                outcome.value,
                type(exc).__name__,
            )
        if outcome != ProcessOutcome.PERMANENT:
            return outcome
        return await self._fail(batch_id, document_id, correlation_id, str(exc))

    def _classify_model_error(
        self, exc: ModelRouterFailedError, batch_id: str, correlation_id: str | None
    ) -> ProcessOutcome:
        sc = exc.status_code
        if sc is None or sc == 429 or 500 <= sc <= 599:
            outcome = ProcessOutcome.TRANSIENT
        else:
            outcome = ProcessOutcome.PERMANENT
        logger.error(
            "batch_id=%s correlation_id=%s outcome=%s status_code=%s error_class=%s",
            batch_id,
            correlation_id,
            outcome.value,
            sc,
            "ModelRouterFailedError",
        )
        return outcome

    async def _fail(
        self,
        batch_id: str,
        document_id: str | None,
        correlation_id: str | None,
        reason: str,
    ) -> ProcessOutcome:
        event = make_seeding_failed_event(
            batch_id=batch_id,
            document_id=document_id,
            reason=reason,
            job_id=batch_id,
            trigger_type=_TRIGGER_TYPE,
            correlation_id=correlation_id,
        )
        await self._deps.publisher.publish(
            self._deps.settings.seeding_failed_topic,
            event,
            session_id=batch_id,
            correlation_id=correlation_id,
        )
        return ProcessOutcome.PERMANENT
