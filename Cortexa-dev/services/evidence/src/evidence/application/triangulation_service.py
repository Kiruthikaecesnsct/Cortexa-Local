import asyncio
import hashlib
import logging
import time
from dataclasses import dataclass
from datetime import UTC, datetime
from typing import Any
from uuid import uuid4

import sentry_sdk

from evidence.application.concurrency.evidence_scheduler import EvidenceScheduler
from evidence.domain.enums.confidence_band import ConfidenceBand
from evidence.domain.enums.evidence_source import EvidenceSource
from evidence.domain.enums.patent_source_name import PatentSourceName
from evidence.domain.enums.patent_source_outcome import PatentSourceOutcome
from evidence.domain.enums.source_status import SourceStatus
from evidence.domain.errors.evidence_errors import (
    EvidenceBundleBelowMinimumSourcesError,
    EvidenceBundleSourceUnavailableError,
    EvidenceCandidateDeadlineExceededError,
)
from evidence.domain.models.evidence_bundle import EvidenceBundle, LlmResearchSummary
from evidence.domain.models.evidence_hit import EvidenceHit
from evidence.domain.models.patent_match import PatentMatch
from evidence.domain.models.patent_source_result import PatentSourceResult
from evidence.domain.models.research_finding import ResearchFinding
from evidence.domain.repositories.composite_patent_adapter_protocol import (
    CompositePatentAdapterProtocol,
)
from evidence.infrastructure.config.patent_config_provider import PatentConfigProvider
from evidence.infrastructure.config.secrets_provider import SecretsProvider
from evidence.infrastructure.config.settings import EvidenceSettings
from evidence.infrastructure.corpus.corpus_adapter import CorpusAdapter
from evidence.infrastructure.llm_research.llm_research_adapter import LlmResearchAdapter

_logger = logging.getLogger(__name__)

_PERMANENT_STATUS_CODES = frozenset({400, 401, 403, 404, 422})


def _reraise_cancelled(*results: object) -> None:
    for r in results:
        if isinstance(r, asyncio.CancelledError):
            raise r


def _reraise_permanent_llm(llm_result: object) -> None:
    from evidence.domain.errors.evidence_errors import LlmResearchError

    if isinstance(llm_result, LlmResearchError):
        if getattr(llm_result, "content_filter", False):
            return
        if getattr(llm_result, "status_code", None) in _PERMANENT_STATUS_CODES:
            raise llm_result


def _status_for_hit_count(hit_count: int) -> SourceStatus:
    return SourceStatus.active if hit_count > 0 else SourceStatus.empty


def _status_for_error(exc: BaseException) -> SourceStatus:
    from evidence.domain.errors.evidence_errors import LlmResearchError

    if isinstance(exc, TimeoutError):
        return SourceStatus.timeout
    if isinstance(exc, LlmResearchError) and exc.content_filter:
        return SourceStatus.filtered
    return SourceStatus.error


@dataclass
class _SourceOutcomes:
    flags: dict[EvidenceSource, bool]
    statuses: dict[EvidenceSource, SourceStatus]


def _compute_degraded(
    source_results: list[PatentSourceResult], min_live: int
) -> tuple[bool, list[PatentSourceName]]:
    live_count = sum(1 for r in source_results if r.outcome == PatentSourceOutcome.ok)
    failed = [r.source for r in source_results if r.outcome != PatentSourceOutcome.ok]
    return live_count < min_live, failed


def _effective_min_live_patent_sources(configured_min: int, enabled_count: int) -> int:
    return max(1, min(configured_min, enabled_count))


def _sha256_of(text: str) -> str:
    return hashlib.sha256(text.strip().lower().encode()).hexdigest()


def _patent_match_to_hit(match: PatentMatch) -> EvidenceHit:
    return EvidenceHit(
        patent_id=match.reference,
        content_hash=_sha256_of(match.title),
        title=match.title,
        citation=match.reference,
        url=match.url,
        similarity=match.relevance_score,
        sources={match.source},
        abstract=match.abstract,
        claims=list(match.claims),
        jurisdiction=match.jurisdiction,
    )


def _research_finding_to_hits(finding: ResearchFinding) -> list[EvidenceHit]:
    return [
        EvidenceHit(
            patent_id=None,
            content_hash=_sha256_of(citation.id),
            title=citation.id,
            citation=citation.id,
            url="",
            similarity=citation.confidence,
            sources={EvidenceSource.LlmResearch},
        )
        for citation in finding.citations
    ]


def _research_finding_to_summary(finding: ResearchFinding) -> LlmResearchSummary:
    return LlmResearchSummary(findings=list(finding.findings), confidence=finding.confidence)


def _handle_llm_branch(
    llm_result: object,
) -> tuple[bool, SourceStatus, list[EvidenceHit], LlmResearchSummary | None]:
    if isinstance(llm_result, BaseException):
        _logger.warning("Evidence source %s failed: %s", EvidenceSource.LlmResearch, llm_result)
        return False, _status_for_error(llm_result), [], None
    hits = _research_finding_to_hits(llm_result)
    summary = _research_finding_to_summary(llm_result)
    return True, _status_for_hit_count(len(hits)), hits, summary


def _dedup_hits(hits: list[EvidenceHit]) -> list[EvidenceHit]:
    groups: dict[str, EvidenceHit] = {}
    for hit in hits:
        key = hit.patent_id if hit.patent_id is not None else hit.content_hash
        if key not in groups:
            groups[key] = hit
            continue
        existing = groups[key]
        groups[key] = existing.model_copy(
            update={
                "sources": existing.sources | hit.sources,
                "similarity": max(existing.similarity, hit.similarity),
            }
        )
    return list(groups.values())


def _warn_if_triangulation_degraded(
    flags: dict[EvidenceSource, bool],
    ids: tuple[str, str, str, str],
    statuses: dict[EvidenceSource, SourceStatus],
    patent_source_results: list[PatentSourceResult],
) -> None:
    active_count = sum(1 for active in flags.values() if active)
    if active_count >= 3:
        return
    candidate_id, document_id, batch_id, job_id = ids
    missing = [src.value for src, active in flags.items() if not active]
    status_summary = {src.value: status.value for src, status in statuses.items()}
    patent_subsources = {res.source.value: res.outcome.value for res in patent_source_results}

    _logger.warning(
        "Evidence triangulation degraded: %d/3 sources active, missing=%s "
        "candidate_id=%s document_id=%s batch_id=%s job_id=%s",
        active_count,
        missing,
        candidate_id,
        document_id,
        batch_id,
        job_id,
        extra={
            "source_statuses": status_summary,
            "patent_subsources": patent_subsources,
        },
    )
    sentry_sdk.capture_message(
        f"Evidence triangulation degraded: {active_count}/3 sources active, missing={missing}; "
        f"statuses={status_summary}; patent_subsources={patent_subsources}",
        level="warning",
    )


def _evaluate_source_policy(flags: dict[EvidenceSource, bool], minimum: int) -> tuple[bool, int]:
    active_count = sum(1 for v in flags.values() if v)
    meets = active_count >= minimum
    return meets, active_count


def _confidence_band(source_flags: dict[EvidenceSource, bool]) -> ConfidenceBand:
    active_count = sum(1 for v in source_flags.values() if v)
    if active_count >= 3:
        return ConfidenceBand.High
    if active_count == 2:
        return ConfidenceBand.Medium
    return ConfidenceBand.Low


def _resolve_patent_result(
    patent_result: object,
) -> tuple[list[PatentMatch], list[PatentSourceResult]]:
    if isinstance(patent_result, BaseException):
        return [], []
    matches, source_results = patent_result  # type: ignore[misc]
    return matches, source_results


def _handle_patent_branch(
    patent_result: object,
) -> tuple[list[PatentSourceResult], bool, SourceStatus, list[EvidenceHit]]:
    matches, source_results = _resolve_patent_result(patent_result)
    ok = not isinstance(patent_result, BaseException)
    if not ok:
        _logger.warning("Evidence source %s failed: %s", EvidenceSource.PatentApi, patent_result)
        status = _status_for_error(patent_result)  # type: ignore[arg-type]
    else:
        status = _status_for_hit_count(len(matches))
    patent_hits = [_patent_match_to_hit(m) for m in matches]
    return source_results, ok, status, patent_hits


class TriangulationService:
    def __init__(
        self,
        patent_adapter: CompositePatentAdapterProtocol,
        corpus_adapter: CorpusAdapter,
        llm_adapter: LlmResearchAdapter,
        settings: EvidenceSettings,
        scheduler: EvidenceScheduler,
        patent_config_provider: PatentConfigProvider | None = None,
        secrets_provider: SecretsProvider | None = None,
    ) -> None:
        self._patent_adapter = patent_adapter
        self._corpus_adapter = corpus_adapter
        self._llm_adapter = llm_adapter
        self._settings = settings
        self._scheduler = scheduler
        self._patent_config_provider = patent_config_provider
        self._secrets_provider = secrets_provider

    async def _resolve_enabled_patent_sources(self) -> frozenset[PatentSourceName]:
        if self._patent_config_provider is None:
            return frozenset(PatentSourceName)
        config = await self._patent_config_provider.get_config()
        if self._secrets_provider is not None:
            self._secrets_provider.invalidate_on_version_change(config.version)
        return config.enabled

    async def triangulate(
        self,
        candidate_id: str,
        document_id: str,
        claim_text: str,
        tech_field: str,
        batch_id: str = "",
        job_id: str = "",
        ai_model: str | None = None,
    ) -> EvidenceBundle:
        query = f"{claim_text} {tech_field}".strip()
        limit = self._settings.corpus_search_top_k
        effective_model = self._settings.llm_research_model or ai_model
        enabled_patent_sources = await self._resolve_enabled_patent_sources()

        params = (query, limit, claim_text, tech_field, effective_model, enabled_patent_sources)
        patent_result, corpus_result, llm_result = await self._gather_sources_concurrent(params)

        _reraise_cancelled(patent_result, corpus_result, llm_result)
        _reraise_permanent_llm(llm_result)

        outcomes, all_hits, patent_source_results, llm_research = self._build_flags_and_hits(
            patent_result, corpus_result, llm_result
        )

        if not any(outcomes.flags.values()):
            raise EvidenceBundleSourceUnavailableError("All evidence sources returned no results")

        meets_minimum, active_count = _evaluate_source_policy(
            outcomes.flags, self._settings.minimum_active_sources
        )
        if self._settings.minimum_source_policy == "fail" and not meets_minimum:
            min_required = self._settings.minimum_active_sources
            msg = f"Triangulation below minimum: {active_count}/{min_required} sources active"
            raise EvidenceBundleBelowMinimumSourcesError(msg)

        _warn_if_triangulation_degraded(
            outcomes.flags,
            (candidate_id, document_id, batch_id, job_id),
            outcomes.statuses,
            patent_source_results,
        )

        return self._build_bundle(
            outcomes,
            all_hits,
            patent_source_results,
            llm_research,
            (candidate_id, document_id, batch_id, job_id),
            len(enabled_patent_sources),
        )

    def _create_source_tasks(
        self,
        params: tuple[str, int, str, str, str | None, frozenset[PatentSourceName]],
    ) -> dict[EvidenceSource, asyncio.Task]:
        query, limit, claim_text, tech_field, effective_model, enabled_patent_sources = params
        return {
            EvidenceSource.PatentApi: asyncio.create_task(
                self._run_patent_source(query, limit, enabled_patent_sources)
            ),
            EvidenceSource.SeedCorpus: asyncio.create_task(self._run_corpus_source(query, limit)),
            EvidenceSource.LlmResearch: asyncio.create_task(
                self._run_llm_source(claim_text, tech_field, effective_model)
            ),
        }

    @staticmethod
    def _task_outcome(task: asyncio.Task) -> object:
        if task.cancelled():
            return asyncio.CancelledError()
        exc = task.exception()
        return exc if exc is not None else task.result()

    def _resolve_task_results(
        self, tasks: dict[EvidenceSource, asyncio.Task]
    ) -> tuple[Any, Any, Any]:
        return (
            self._task_outcome(tasks[EvidenceSource.PatentApi]),
            self._task_outcome(tasks[EvidenceSource.SeedCorpus]),
            self._task_outcome(tasks[EvidenceSource.LlmResearch]),
        )

    @staticmethod
    async def _cancel_pending_tasks(pending: set[asyncio.Task]) -> None:
        for task in pending:
            task.cancel()
        for task in pending:
            try:
                await task
            except BaseException:  # noqa: BLE001
                pass

    def _deadline_task_outcome(self, task: asyncio.Task, was_pending: bool) -> object:
        if was_pending:
            return TimeoutError("Candidate deadline exceeded before source completed")
        return self._task_outcome(task)

    async def _handle_deadline_exceeded(
        self,
        tasks: dict[EvidenceSource, asyncio.Task],
        pending: set[asyncio.Task],
        elapsed_seconds: float,
    ) -> tuple[Any, Any, Any]:
        pending_sources = [source.value for source, task in tasks.items() if task in pending]
        await self._cancel_pending_tasks(pending)

        results = {
            source: self._deadline_task_outcome(task, task in pending)
            for source, task in tasks.items()
        }
        completed_count = sum(
            1 for result in results.values() if not isinstance(result, BaseException)
        )
        if completed_count >= self._settings.minimum_active_sources:
            return (
                results[EvidenceSource.PatentApi],
                results[EvidenceSource.SeedCorpus],
                results[EvidenceSource.LlmResearch],
            )

        raise EvidenceCandidateDeadlineExceededError(
            pending_sources=pending_sources, elapsed_seconds=elapsed_seconds
        )

    async def _gather_sources_concurrent(
        self,
        params: tuple[str, int, str, str, str | None, frozenset[PatentSourceName]],
    ) -> tuple[Any, Any, Any]:
        deadline = self._scheduler.candidate_deadline_seconds()

        tasks = self._create_source_tasks(params)
        start = time.monotonic()
        try:
            _done, pending = await asyncio.wait(tasks.values(), timeout=deadline)
        except asyncio.CancelledError:
            await self._cancel_pending_tasks(set(tasks.values()))
            raise
        elapsed_seconds = time.monotonic() - start

        if not pending:
            return self._resolve_task_results(tasks)

        return await self._handle_deadline_exceeded(tasks, pending, elapsed_seconds)

    def _build_flags_and_hits(
        self,
        patent_result: object,
        corpus_result: object,
        llm_result: object,
    ) -> tuple[
        _SourceOutcomes, list[EvidenceHit], list[PatentSourceResult], LlmResearchSummary | None
    ]:
        patent_source_results, patent_ok, patent_status, patent_hits = _handle_patent_branch(
            patent_result
        )
        flags: dict[EvidenceSource, bool] = {EvidenceSource.PatentApi: patent_ok}
        statuses: dict[EvidenceSource, SourceStatus] = {EvidenceSource.PatentApi: patent_status}

        flags[EvidenceSource.SeedCorpus], statuses[EvidenceSource.SeedCorpus], corpus_hits = (
            self._process_source(
                corpus_result,
                EvidenceSource.SeedCorpus,
                lambda r: [_patent_match_to_hit(m) for m in r],
            )
        )
        (
            flags[EvidenceSource.LlmResearch],
            statuses[EvidenceSource.LlmResearch],
            llm_hits,
            llm_research,
        ) = _handle_llm_branch(llm_result)

        all_hits = patent_hits + corpus_hits + llm_hits
        return (
            _SourceOutcomes(flags=flags, statuses=statuses),
            all_hits,
            patent_source_results,
            llm_research,
        )

    async def _run_patent_source(
        self, query: str, limit: int, enabled: frozenset[PatentSourceName]
    ) -> object:
        try:
            async with self._scheduler.acquire_patent_slot():
                return await self._patent_adapter.search(query, limit=limit, enabled=enabled)
        except Exception as exc:
            return exc

    async def _run_corpus_source(self, query: str, limit: int) -> object:
        try:
            async with self._scheduler.acquire_corpus_slot():
                return await self._corpus_adapter.search(query, limit=limit)
        except Exception as exc:
            return exc

    async def _run_llm_source(
        self,
        claim_text: str,
        tech_field: str,
        model: str | None,
    ) -> object:
        try:
            async with self._scheduler.acquire_llm_slot():
                return await self._llm_adapter.research(
                    candidate_description=claim_text,
                    source_text=tech_field,
                    model=model,
                )
        except Exception as exc:
            return exc

    def _build_bundle(
        self,
        outcomes: _SourceOutcomes,
        all_hits: list[EvidenceHit],
        source_results: list[PatentSourceResult],
        llm_research: LlmResearchSummary | None,
        ids: tuple[str, str, str, str],
        enabled_patent_source_count: int,
    ) -> EvidenceBundle:
        candidate_id, document_id, batch_id, job_id = ids
        deduped = _dedup_hits(all_hits)
        sources_used = [src for src, active in outcomes.flags.items() if active]
        band = _confidence_band(outcomes.flags)
        effective_min_live = _effective_min_live_patent_sources(
            self._settings.min_live_patent_sources, enabled_patent_source_count
        )
        degraded, degraded_sources = _compute_degraded(source_results, effective_min_live)
        meets_minimum, active_count = _evaluate_source_policy(
            outcomes.flags, self._settings.minimum_active_sources
        )
        return EvidenceBundle(
            id=str(uuid4()),
            batch_id=batch_id,
            job_id=job_id,
            candidate_id=candidate_id,
            document_id=document_id,
            hits=deduped,
            confidence_band=band,
            sources_used=sources_used,
            source_flags=outcomes.flags,
            source_status=outcomes.statuses,
            merged_at=datetime.now(UTC),
            patent_source_results=source_results,
            degraded=degraded,
            degraded_sources=degraded_sources,
            active_source_count=active_count,
            meets_minimum_sources=meets_minimum,
            minimum_active_sources=self._settings.minimum_active_sources,
            llm_research=llm_research,
        )

    def _process_source(
        self,
        result: object,
        source: EvidenceSource,
        converter,
    ) -> tuple[bool, SourceStatus, list[EvidenceHit]]:
        if isinstance(result, BaseException):
            _logger.warning("Evidence source %s failed: %s", source, result)
            return False, _status_for_error(result), []
        hits = converter(result)
        return True, _status_for_hit_count(len(hits)), hits
