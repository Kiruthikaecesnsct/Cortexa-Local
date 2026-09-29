import asyncio
import logging
import time
from collections.abc import Callable

import httpx
import pydantic

from evidence.domain.enums.patent_source_name import PatentSourceName
from evidence.domain.enums.patent_source_outcome import PatentSourceOutcome
from evidence.domain.errors.evidence_errors import PatentApiError, PatentAuthError
from evidence.domain.models.patent_match import PatentMatch
from evidence.domain.models.patent_source_result import PatentSourceResult
from evidence.infrastructure.patent_apis.epo_adapter import EpoAdapter
from evidence.infrastructure.patent_apis.lens_adapter import LensAdapter
from evidence.infrastructure.patent_apis.uspto_adapter import UsptoAdapter

_logger = logging.getLogger(__name__)

_ALL_SOURCES: frozenset[PatentSourceName] = frozenset(
    {PatentSourceName.USPTO, PatentSourceName.EPO, PatentSourceName.Lens}
)

_RULES: list[tuple[Callable[[BaseException], bool], PatentSourceOutcome]] = [
    (lambda e: isinstance(e, PatentAuthError), PatentSourceOutcome.auth_error),
    (lambda e: isinstance(e, httpx.TimeoutException), PatentSourceOutcome.timeout),
    (
        lambda e: isinstance(e, httpx.HTTPStatusError) and e.response.status_code == 429,
        PatentSourceOutcome.rate_limited,
    ),
    (
        lambda e: isinstance(e, PatentApiError) and e.status_code == 429,
        PatentSourceOutcome.rate_limited,
    ),
    (
        lambda e: isinstance(e, (httpx.HTTPStatusError, httpx.NetworkError)),
        PatentSourceOutcome.api_error,
    ),
    (
        lambda e: isinstance(e, (pydantic.ValidationError, KeyError, ValueError)),
        PatentSourceOutcome.schema_error,
    ),
]


def _classify_outcome(exc: BaseException) -> PatentSourceOutcome:
    return next(
        (outcome for pred, outcome in _RULES if pred(exc)), PatentSourceOutcome.schema_error
    )


def _build_error_detail(exc: BaseException) -> str:
    type_name = type(exc).__name__
    if isinstance(exc, httpx.HTTPStatusError):
        return f"{type_name}:{exc.response.status_code}"
    if isinstance(exc, PatentApiError) and exc.status_code is not None:
        return f"{type_name}:{exc.status_code}"
    return type_name


def _log_source_result(
    name: PatentSourceName,
    outcome: PatentSourceOutcome,
    hit_count: int,
    latency_ms: float,
    error_detail: str | None,
) -> None:
    _logger.info(
        "patent_source_result",
        extra={
            "source": name.value,
            "outcome": outcome.value,
            "hit_count": hit_count,
            "latency_ms": round(latency_ms, 1),
            "error_detail": error_detail,
        },
    )


async def _run_source(
    name: PatentSourceName,
    adapter,
    query: str,
    limit: int,
) -> tuple[list[PatentMatch], PatentSourceResult]:
    start = time.monotonic()
    try:
        matches = await adapter.search(query, limit)
        latency_ms = (time.monotonic() - start) * 1000
        _log_source_result(name, PatentSourceOutcome.ok, len(matches), latency_ms, None)
        return matches, PatentSourceResult(
            source=name,
            outcome=PatentSourceOutcome.ok,
            hit_count=len(matches),
            latency_ms=round(latency_ms, 1),
        )
    except Exception as exc:
        # BUG147: verified — EPO throttle/exhaustion here degrades gracefully via USPTO+Lens;
        # do not raise the min_live_patent_sources floor to an exception, that would newly
        # drop PatentApi.
        latency_ms = (time.monotonic() - start) * 1000
        outcome = _classify_outcome(exc)
        error_detail = _build_error_detail(exc)
        _log_source_result(name, outcome, 0, latency_ms, error_detail)
        return [], PatentSourceResult(
            source=name,
            outcome=outcome,
            hit_count=0,
            latency_ms=round(latency_ms, 1),
            error_detail=error_detail,
        )


def _backfill_enrichment(winner: PatentMatch, duplicate: PatentMatch) -> PatentMatch:
    updates: dict[str, object] = {}
    if not winner.abstract and duplicate.abstract:
        updates["abstract"] = duplicate.abstract
    if not winner.claims and duplicate.claims:
        updates["claims"] = duplicate.claims
    if not winner.jurisdiction and duplicate.jurisdiction:
        updates["jurisdiction"] = duplicate.jurisdiction
    if not updates:
        return winner
    return winner.model_copy(update=updates)


class CompositePatentAdapter:
    def __init__(self, uspto: UsptoAdapter, epo: EpoAdapter, lens: LensAdapter) -> None:
        self._uspto = uspto
        self._epo = epo
        self._lens = lens

    def _enabled_sources(
        self, enabled: frozenset[PatentSourceName] | None
    ) -> list[tuple[PatentSourceName, UsptoAdapter | EpoAdapter | LensAdapter]]:
        active = enabled if enabled is not None else _ALL_SOURCES
        source_map: dict[PatentSourceName, UsptoAdapter | EpoAdapter | LensAdapter] = {
            PatentSourceName.USPTO: self._uspto,
            PatentSourceName.EPO: self._epo,
            PatentSourceName.Lens: self._lens,
        }
        return [(name, adapter) for name, adapter in source_map.items() if name in active]

    async def search(
        self,
        query: str,
        limit: int,
        enabled: frozenset[PatentSourceName] | None = None,
    ) -> tuple[list[PatentMatch], list[PatentSourceResult]]:
        runnable = self._enabled_sources(enabled)
        source_tasks = (
            await asyncio.gather(
                *(_run_source(name, adapter, query, limit) for name, adapter in runnable)
            )
            if runnable
            else []
        )
        all_source_results: list[PatentSourceResult] = []
        merged: dict[str, PatentMatch] = {}
        for matches, source_result in source_tasks:
            all_source_results.append(source_result)
            for match in matches:
                if not match.reference or not match.reference.strip():
                    continue
                existing = merged.get(match.reference)
                if existing is None:
                    merged[match.reference] = match
                elif match.relevance_score > existing.relevance_score:
                    merged[match.reference] = _backfill_enrichment(match, existing)
                else:
                    merged[match.reference] = _backfill_enrichment(existing, match)
        return list(merged.values()), all_source_results
