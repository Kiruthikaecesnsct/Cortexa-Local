import asyncio
import logging
import time
from dataclasses import dataclass, field

from seeding.application.handlers.process_seeding_request_handler import ProcessOutcome
from seeding.domain.errors.seeding_errors import (
    EvidenceServicePermanentError,
    EvidenceServiceTransientError,
    VectorRouterPermanentError,
)
from seeding.domain.events.landscape import LandscapeEnvelope, make_landscape_completed_event
from seeding.domain.events.seeding_failed import make_seeding_failed_event
from seeding.domain.models.digest import CitedEntry, InventionContextBrief
from seeding.domain.models.landscape import (
    ConceptLandscape,
    CorpusMatch,
    LandscapeSourceFlags,
    LiveMatch,
    PriorArtLandscape,
    WhitespaceIntersection,
)
from seeding.domain.ports.event_publisher_port import EventPublisherPort
from seeding.domain.ports.vector_router_port import VectorRouterPort
from seeding.domain.services.landscape_density import (
    DensityThresholds,
    classify_concept,
    count_relevant,
    max_similarity,
    whitespace_flag,
)
from seeding.infrastructure.clients.evidence_client import EvidenceClient, EvidenceSearchResult
from seeding.infrastructure.config.settings import SeedingSettings
from seeding.infrastructure.cosmos.digest_repository import DigestRepository
from seeding.infrastructure.cosmos.landscape_repository import LandscapeRepository, landscape_id

logger = logging.getLogger(__name__)

_CORPUS_TARGET = "corpus"
_TRIGGER_TYPE = "pipeline"
_MATCH_LIMIT = 5
_MIN_QUERY_TOKENS = 3
_EVIDENCE_ERRORS = (EvidenceServiceTransientError, EvidenceServicePermanentError)
_PERMANENT_ERRORS = (VectorRouterPermanentError,)


class _DeadlineExceeded(Exception):
    pass


@dataclass
class GenerateLandscapeDeps:
    digest_repo: DigestRepository
    landscape_repo: LandscapeRepository
    vector_client: VectorRouterPort
    evidence_client: EvidenceClient
    publisher: EventPublisherPort
    settings: SeedingSettings


@dataclass(frozen=True)
class _LandscapeContext:
    batch_id: str
    document_id: str
    correlation_id: str | None
    ai_model: str | None
    deadline: float


@dataclass(frozen=True)
class _ConceptOutcome:
    landscape: ConceptLandscape
    live: EvidenceSearchResult | None


@dataclass
class _ConceptAggregate:
    items: list[ConceptLandscape] = field(default_factory=list)
    flags: LandscapeSourceFlags = field(default_factory=LandscapeSourceFlags)


def _thresholds(settings: SeedingSettings) -> DensityThresholds:
    return DensityThresholds(
        sim_floor=settings.landscape_sim_floor,
        crowded_max_sim=settings.landscape_crowded_max_sim,
        moderate_max_sim=settings.landscape_moderate_max_sim,
        crowded_corpus_hits=settings.landscape_crowded_corpus_hits,
        moderate_corpus_hits=settings.landscape_moderate_corpus_hits,
        crowded_live_hits=settings.landscape_crowded_live_hits,
        moderate_live_hits=settings.landscape_moderate_live_hits,
        whitespace_sim=settings.landscape_whitespace_sim,
    )


def _live_query(concept_text: str, tech_fields: list[CitedEntry], maxlen: int) -> str:
    query = concept_text.strip()
    if len(query.split()) < _MIN_QUERY_TOKENS and tech_fields:
        query = f"{query} {tech_fields[0].text}".strip()
    return query[:maxlen]


def _corpus_scores(hits: list[dict]) -> list[float]:
    return [float(hit.get("score") or 0.0) for hit in hits]


def _top_corpus(hits: list[dict]) -> list[CorpusMatch]:
    ordered = sorted(hits, key=lambda h: (-float(h.get("score") or 0.0), str(h.get("id") or "")))
    return [_to_corpus_match(hit) for hit in ordered[:_MATCH_LIMIT]]


def _to_corpus_match(hit: dict) -> CorpusMatch:
    payload = hit.get("payload") or {}
    return CorpusMatch(
        id=str(hit.get("id") or ""),
        score=float(hit.get("score") or 0.0),
        chunk_id=str(payload.get("chunk_id") or ""),
        document_id=str(payload.get("document_id") or ""),
        section_label=str(payload.get("section_label") or ""),
        text_excerpt=str(payload.get("text_excerpt") or ""),
    )


def _top_id(hits: list[dict]) -> str:
    if not hits:
        return ""
    top = max(hits, key=lambda h: float(h.get("score") or 0.0))
    return str(top.get("id") or "")


def _dedup_live(result: EvidenceSearchResult | None) -> list:
    if result is None:
        return []
    seen: set[str] = set()
    unique = []
    for match in result.matches:
        if match.reference in seen:
            continue
        seen.add(match.reference)
        unique.append(match)
    return unique


def _top_live(matches: list) -> list[LiveMatch]:
    ordered = sorted(matches, key=lambda m: (-m.relevance_score, m.reference))
    return [
        LiveMatch(
            reference=m.reference,
            title=m.title,
            applicant=m.applicant,
            date=m.date,
            url=m.url,
            relevance_score=m.relevance_score,
            source=m.source,
        )
        for m in ordered[:_MATCH_LIMIT]
    ]


def _aggregate_flags(outcomes: list[_ConceptOutcome]) -> LandscapeSourceFlags:
    reachable = False
    ok: set[str] = set()
    degraded: set[str] = set()
    for outcome in outcomes:
        if outcome.live is None:
            continue
        reachable = True
        for status in outcome.live.sources:
            target = ok if status.outcome == "ok" else degraded
            target.add(status.source)
    return LandscapeSourceFlags(
        evidence_reachable=reachable,
        corpus_only=not reachable,
        live_sources_ok=sorted(ok),
        degraded_sources=sorted(degraded),
    )


class GenerateLandscapeHandler:
    def __init__(self, deps: GenerateLandscapeDeps) -> None:
        self._deps = deps

    async def handle(self, envelope: LandscapeEnvelope) -> ProcessOutcome:
        try:
            return await self._execute(envelope)
        except _DeadlineExceeded:
            return ProcessOutcome.TRANSIENT
        except Exception as exc:
            return await self._classify_error(exc, envelope)

    def _build_context(self, envelope: LandscapeEnvelope) -> _LandscapeContext:
        document_id = envelope.payload.document_id or envelope.document_id or ""
        budget = self._deps.settings.landscape_message_time_budget_seconds
        return _LandscapeContext(
            batch_id=envelope.batch_id,
            document_id=document_id,
            correlation_id=envelope.correlation_id,
            ai_model=envelope.payload.ai_model,
            deadline=time.monotonic() + budget,
        )

    async def _execute(self, envelope: LandscapeEnvelope) -> ProcessOutcome:
        ctx = self._build_context(envelope)
        settings = self._deps.settings
        brief = await self._deps.digest_repo.get_brief(
            ctx.batch_id, ctx.document_id, settings.digest_prompt_version
        )
        if brief is None:
            return self._missing_brief(ctx)
        existing = await self._deps.landscape_repo.get_landscape(
            ctx.batch_id, ctx.document_id, settings.landscape_schema_version
        )
        if existing is not None:
            return await self._republish(ctx, existing)
        embed_map = await self._embed_all(brief)
        aggregate = await self._process_concepts(brief, embed_map, ctx)
        whitespace = await self._process_whitespace(brief, embed_map, ctx)
        landscape = self._build_landscape(ctx, aggregate, whitespace)
        await self._deps.landscape_repo.save_landscape(landscape)
        return await self._publish_completed(ctx, landscape)

    def _missing_brief(self, ctx: _LandscapeContext) -> ProcessOutcome:
        logger.info(
            "batch_id=%s document_id=%s outcome=TRANSIENT reason=brief_not_ready",
            ctx.batch_id,
            ctx.document_id,
        )
        return ProcessOutcome.TRANSIENT

    def _ensure_deadline(self, ctx: _LandscapeContext) -> None:
        if time.monotonic() >= ctx.deadline:
            raise _DeadlineExceeded

    def _collect_texts(self, brief: InventionContextBrief) -> list[str]:
        entries = brief.key_concepts + brief.contributions + brief.future_work
        return [entry.text.strip() for entry in entries if entry.text.strip()]

    async def _embed_all(self, brief: InventionContextBrief) -> dict[str, list[float]]:
        unique: list[str] = []
        seen: set[str] = set()
        for text in self._collect_texts(brief):
            if text not in seen:
                seen.add(text)
                unique.append(text)
        vectors = await self._embed_batches(unique)
        return dict(zip(unique, vectors, strict=True))

    async def _embed_batches(self, texts: list[str]) -> list[list[float]]:
        max_texts = self._deps.settings.landscape_embed_max_texts
        vectors: list[list[float]] = []
        for start in range(0, len(texts), max_texts):
            vectors.extend(await self._deps.vector_client.embed(texts[start : start + max_texts]))
        return vectors

    async def _process_concepts(
        self,
        brief: InventionContextBrief,
        embed_map: dict[str, list[float]],
        ctx: _LandscapeContext,
    ) -> _ConceptAggregate:
        sem = asyncio.Semaphore(self._deps.settings.landscape_search_concurrency)
        concepts = [entry for entry in brief.key_concepts if entry.text.strip()]
        tasks = [
            self._process_concept(entry, embed_map, brief.tech_fields, sem, ctx)
            for entry in concepts
        ]
        outcomes = await asyncio.gather(*tasks)
        return _ConceptAggregate(
            items=[outcome.landscape for outcome in outcomes],
            flags=_aggregate_flags(outcomes),
        )

    async def _process_concept(
        self,
        concept: CitedEntry,
        embed_map: dict[str, list[float]],
        tech_fields: list[CitedEntry],
        sem: asyncio.Semaphore,
        ctx: _LandscapeContext,
    ) -> _ConceptOutcome:
        async with sem:
            self._ensure_deadline(ctx)
            corpus_hits = await self._corpus_search(
                concept.text, embed_map, self._deps.settings.landscape_top_k
            )
            live = await self._live_search(concept.text, tech_fields, ctx)
        landscape = self._build_concept_landscape(concept, corpus_hits, live)
        return _ConceptOutcome(landscape=landscape, live=live)

    async def _corpus_search(
        self, text: str, embed_map: dict[str, list[float]], top_k: int
    ) -> list[dict]:
        vector = embed_map.get(text.strip())
        if vector is None:
            return []
        return await self._deps.vector_client.search(vector, top_k, _CORPUS_TARGET, None)

    async def _live_search(
        self, concept_text: str, tech_fields: list[CitedEntry], ctx: _LandscapeContext
    ) -> EvidenceSearchResult | None:
        settings = self._deps.settings
        query = _live_query(concept_text, tech_fields, settings.landscape_live_query_maxlen)
        try:
            return await self._deps.evidence_client.search_patents(
                query, settings.landscape_live_limit
            )
        except _EVIDENCE_ERRORS as exc:
            logger.warning(
                "batch_id=%s document_id=%s degrade=corpus_only error_class=%s",
                ctx.batch_id,
                ctx.document_id,
                type(exc).__name__,
            )
            return None

    def _build_concept_landscape(
        self, concept: CitedEntry, corpus_hits: list[dict], live: EvidenceSearchResult | None
    ) -> ConceptLandscape:
        thresholds = _thresholds(self._deps.settings)
        scores = _corpus_scores(corpus_hits)
        live_matches = _dedup_live(live)
        live_hit_count = len(live_matches)
        density, corpus_ax, live_ax = classify_concept(scores, live_hit_count, thresholds)
        return ConceptLandscape(
            concept=concept.text,
            chunk_ids=concept.chunk_ids,
            density=density,
            corpus_axis=corpus_ax,
            live_axis=live_ax,
            max_sim=max_similarity(scores),
            relevant_corpus_hits=count_relevant(scores, thresholds.sim_floor),
            live_hit_count=live_hit_count,
            corpus_matches=_top_corpus(corpus_hits),
            live_matches=_top_live(live_matches),
        )

    async def _process_whitespace(
        self,
        brief: InventionContextBrief,
        embed_map: dict[str, list[float]],
        ctx: _LandscapeContext,
    ) -> list[WhitespaceIntersection]:
        sem = asyncio.Semaphore(self._deps.settings.landscape_search_concurrency)
        units = [("contribution", entry) for entry in brief.contributions]
        units += [("future_work", entry) for entry in brief.future_work]
        tasks = [
            self._process_whitespace_unit(kind, entry, embed_map, sem, ctx)
            for kind, entry in units
            if entry.text.strip()
        ]
        results = await asyncio.gather(*tasks)
        return [item for item in results if item is not None]

    async def _process_whitespace_unit(
        self,
        kind: str,
        entry: CitedEntry,
        embed_map: dict[str, list[float]],
        sem: asyncio.Semaphore,
        ctx: _LandscapeContext,
    ) -> WhitespaceIntersection | None:
        async with sem:
            self._ensure_deadline(ctx)
            hits = await self._corpus_search(
                entry.text, embed_map, self._deps.settings.landscape_whitespace_top_k
            )
        scores = _corpus_scores(hits)
        if not whitespace_flag(scores, self._deps.settings.landscape_whitespace_sim):
            return None
        return WhitespaceIntersection(
            kind=kind,
            text=entry.text,
            chunk_ids=entry.chunk_ids,
            nearest_similarity=max_similarity(scores),
            nearest_reference=_top_id(hits),
        )

    def _build_landscape(
        self,
        ctx: _LandscapeContext,
        aggregate: _ConceptAggregate,
        whitespace: list[WhitespaceIntersection],
    ) -> PriorArtLandscape:
        settings = self._deps.settings
        return PriorArtLandscape(
            id=landscape_id(ctx.batch_id, ctx.document_id, settings.landscape_schema_version),
            batch_id=ctx.batch_id,
            document_id=ctx.document_id,
            schema_version=settings.landscape_schema_version,
            concepts=aggregate.items,
            whitespace=whitespace,
            source_flags=aggregate.flags,
        )

    async def _republish(
        self, ctx: _LandscapeContext, landscape: PriorArtLandscape
    ) -> ProcessOutcome:
        logger.info(
            "batch_id=%s document_id=%s landscape_id=%s outcome=SUCCESS reason=idempotent",
            ctx.batch_id,
            ctx.document_id,
            landscape.id,
        )
        return await self._publish_completed(ctx, landscape)

    async def _publish_completed(
        self, ctx: _LandscapeContext, landscape: PriorArtLandscape
    ) -> ProcessOutcome:
        event = make_landscape_completed_event(
            batch_id=ctx.batch_id,
            document_id=ctx.document_id,
            landscape_id=landscape.id,
            concept_count=len(landscape.concepts),
            whitespace_count=len(landscape.whitespace),
            corpus_only=landscape.source_flags.corpus_only,
            correlation_id=ctx.correlation_id,
        )
        await self._deps.publisher.publish(
            self._deps.settings.landscape_completed_topic,
            event,
            session_id=ctx.batch_id,
            correlation_id=ctx.correlation_id,
        )
        logger.info(
            "batch_id=%s document_id=%s landscape_id=%s concept_count=%d "
            "whitespace_count=%d corpus_only=%s outcome=SUCCESS",
            ctx.batch_id,
            ctx.document_id,
            landscape.id,
            len(landscape.concepts),
            len(landscape.whitespace),
            landscape.source_flags.corpus_only,
        )
        return ProcessOutcome.SUCCESS

    async def _classify_error(self, exc: Exception, envelope: LandscapeEnvelope) -> ProcessOutcome:
        batch_id = envelope.batch_id
        document_id = envelope.payload.document_id or envelope.document_id
        correlation_id = envelope.correlation_id
        if isinstance(exc, _PERMANENT_ERRORS):
            outcome = ProcessOutcome.PERMANENT
        else:
            outcome = ProcessOutcome.TRANSIENT
        logger.error(
            "batch_id=%s correlation_id=%s outcome=%s status_code=%s error_class=%s",
            batch_id,
            correlation_id,
            outcome.value,
            getattr(exc, "status_code", None),
            type(exc).__name__,
        )
        if outcome != ProcessOutcome.PERMANENT:
            return outcome
        return await self._fail(batch_id, document_id, correlation_id, str(exc))

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
