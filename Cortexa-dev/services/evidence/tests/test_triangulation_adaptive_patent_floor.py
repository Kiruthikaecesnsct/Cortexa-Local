import asyncio
from unittest.mock import AsyncMock, MagicMock

import pytest

from evidence.application.concurrency.evidence_scheduler import EvidenceScheduler
from evidence.application.triangulation_service import (
    TriangulationService,
    _effective_min_live_patent_sources,
)
from evidence.domain.enums.evidence_source import EvidenceSource
from evidence.domain.enums.patent_source_name import PatentSourceName
from evidence.domain.enums.patent_source_outcome import PatentSourceOutcome
from evidence.domain.enums.source_status import SourceStatus
from evidence.domain.models.patent_match import PatentMatch
from evidence.domain.models.patent_source_result import PatentSourceResult
from evidence.domain.models.research_finding import Citation, ResearchFinding
from evidence.infrastructure.config.patent_config_provider import PatentSourceConfig
from evidence.infrastructure.config.settings import EvidenceSettings

CANDIDATE_ID = "cand-1"
DOCUMENT_ID = "doc-1"
CLAIM_TEXT = "test claim"
TECH_FIELD = "test field"

ALL_SOURCES = frozenset({PatentSourceName.USPTO, PatentSourceName.EPO, PatentSourceName.Lens})


# ---------------------------------------------------------------------------
# _effective_min_live_patent_sources — pure function
# ---------------------------------------------------------------------------


def test_all_enabled_floor_matches_configured_min():
    assert _effective_min_live_patent_sources(configured_min=2, enabled_count=3) == 2


def test_disable_one_source_floor_unchanged_when_still_above_min():
    assert _effective_min_live_patent_sources(configured_min=2, enabled_count=2) == 2


def test_disable_two_sources_floor_drops_to_one():
    assert _effective_min_live_patent_sources(configured_min=2, enabled_count=1) == 1


def test_all_disabled_floor_has_hard_minimum_of_one():
    assert _effective_min_live_patent_sources(configured_min=2, enabled_count=0) == 1


def test_configured_min_one_with_all_enabled_stays_one():
    assert _effective_min_live_patent_sources(configured_min=1, enabled_count=3) == 1


def test_configured_min_zero_still_has_hard_minimum_of_one():
    assert _effective_min_live_patent_sources(configured_min=0, enabled_count=3) == 1


def test_configured_min_exceeds_enabled_count_caps_to_enabled_count():
    assert _effective_min_live_patent_sources(configured_min=5, enabled_count=2) == 2


# ---------------------------------------------------------------------------
# Integration: TriangulationService honors resolved enabled sources
# ---------------------------------------------------------------------------


def _ok_result(source: PatentSourceName, hit_count: int = 1) -> PatentSourceResult:
    return PatentSourceResult(
        source=source, outcome=PatentSourceOutcome.ok, hit_count=hit_count, latency_ms=10.0
    )


def _make_patent_match(reference: str = "US11234567") -> PatentMatch:
    return PatentMatch(
        reference=reference,
        title="Test Patent",
        applicant="Test Corp",
        date="2024-01-01",
        url="https://patents/test",
        relevance_score=0.9,
        source=EvidenceSource.PatentApi,
    )


def _make_research_finding() -> ResearchFinding:
    return ResearchFinding(
        findings=["test finding"],
        confidence=0.8,
        citations=[Citation(id="US99999999: test", confidence=0.8)],
    )


def _make_service(
    enabled_sources: frozenset[PatentSourceName],
    config_version: int = 1,
    patent_source_results: list[PatentSourceResult] | None = None,
) -> tuple[TriangulationService, AsyncMock, MagicMock]:
    patent_adapter = AsyncMock()
    corpus_adapter = AsyncMock()
    llm_adapter = AsyncMock()

    patent_adapter.search.return_value = (
        [_make_patent_match()],
        patent_source_results or [_ok_result(s) for s in enabled_sources],
    )
    corpus_adapter.search.return_value = [_make_patent_match(reference="US99999999")]
    llm_adapter.research.return_value = _make_research_finding()

    settings = EvidenceSettings(
        model_router_url="http://test-model-router",
        vector_router_url="http://test-vector-router",
        min_live_patent_sources=2,
    )
    scheduler = EvidenceScheduler(settings)

    patent_config_provider = AsyncMock()
    patent_config_provider.get_config.return_value = PatentSourceConfig(
        enabled=enabled_sources, version=config_version
    )
    secrets_provider = MagicMock()

    service = TriangulationService(
        patent_adapter=patent_adapter,
        corpus_adapter=corpus_adapter,
        llm_adapter=llm_adapter,
        settings=settings,
        scheduler=scheduler,
        patent_config_provider=patent_config_provider,
        secrets_provider=secrets_provider,
    )
    return service, patent_adapter, secrets_provider


@pytest.mark.asyncio
async def test_no_config_provider_defaults_to_all_sources_enabled():
    patent_adapter = AsyncMock()
    corpus_adapter = AsyncMock()
    llm_adapter = AsyncMock()

    patent_adapter.search.return_value = (
        [_make_patent_match()],
        [_ok_result(s) for s in ALL_SOURCES],
    )
    corpus_adapter.search.return_value = [_make_patent_match(reference="US99999999")]
    llm_adapter.research.return_value = _make_research_finding()

    settings = EvidenceSettings(
        model_router_url="http://test-model-router",
        vector_router_url="http://test-vector-router",
    )
    scheduler = EvidenceScheduler(settings)
    service = TriangulationService(
        patent_adapter=patent_adapter,
        corpus_adapter=corpus_adapter,
        llm_adapter=llm_adapter,
        settings=settings,
        scheduler=scheduler,
    )

    await service.triangulate(CANDIDATE_ID, DOCUMENT_ID, CLAIM_TEXT, TECH_FIELD)

    call_kwargs = patent_adapter.search.call_args.kwargs
    assert call_kwargs["enabled"] == ALL_SOURCES


@pytest.mark.asyncio
async def test_lens_disabled_passed_through_to_composite_adapter():
    enabled = frozenset({PatentSourceName.USPTO, PatentSourceName.EPO})
    service, patent_adapter, _ = _make_service(enabled_sources=enabled)

    await service.triangulate(CANDIDATE_ID, DOCUMENT_ID, CLAIM_TEXT, TECH_FIELD)

    call_kwargs = patent_adapter.search.call_args.kwargs
    assert call_kwargs["enabled"] == enabled
    assert PatentSourceName.Lens not in call_kwargs["enabled"]


@pytest.mark.asyncio
async def test_lens_disabled_both_remaining_sources_ok_not_degraded():
    enabled = frozenset({PatentSourceName.USPTO, PatentSourceName.EPO})
    service, _, _ = _make_service(
        enabled_sources=enabled,
        patent_source_results=[
            _ok_result(PatentSourceName.USPTO),
            _ok_result(PatentSourceName.EPO),
        ],
    )

    bundle = await service.triangulate(CANDIDATE_ID, DOCUMENT_ID, CLAIM_TEXT, TECH_FIELD)

    assert bundle.degraded is False


@pytest.mark.asyncio
async def test_two_sources_disabled_single_source_ok_not_degraded():
    enabled = frozenset({PatentSourceName.USPTO})
    service, patent_adapter, _ = _make_service(
        enabled_sources=enabled,
        patent_source_results=[_ok_result(PatentSourceName.USPTO)],
    )

    bundle = await service.triangulate(CANDIDATE_ID, DOCUMENT_ID, CLAIM_TEXT, TECH_FIELD)

    call_kwargs = patent_adapter.search.call_args.kwargs
    assert call_kwargs["enabled"] == enabled
    assert bundle.degraded is False


@pytest.mark.asyncio
async def test_two_sources_disabled_single_source_fails_marks_degraded_but_no_wedge():
    enabled = frozenset({PatentSourceName.USPTO})
    failed_result = PatentSourceResult(
        source=PatentSourceName.USPTO,
        outcome=PatentSourceOutcome.timeout,
        hit_count=0,
        latency_ms=5000.0,
    )
    service, _, _ = _make_service(enabled_sources=enabled, patent_source_results=[failed_result])

    bundle = await service.triangulate(CANDIDATE_ID, DOCUMENT_ID, CLAIM_TEXT, TECH_FIELD)

    assert bundle.degraded is True
    assert bundle.degraded_sources == [PatentSourceName.USPTO]


@pytest.mark.asyncio
async def test_secrets_provider_invalidate_called_with_resolved_config_version():
    enabled = frozenset({PatentSourceName.USPTO, PatentSourceName.EPO})
    service, _, secrets_provider = _make_service(enabled_sources=enabled, config_version=7)

    await service.triangulate(CANDIDATE_ID, DOCUMENT_ID, CLAIM_TEXT, TECH_FIELD)

    secrets_provider.invalidate_on_version_change.assert_called_once_with(7)


@pytest.mark.asyncio
async def test_patent_timeout_marks_patent_api_source_false_and_empty_results():
    async def slow_patent_search(*args, **kwargs):
        await asyncio.sleep(10)
        return [_make_patent_match()], [_ok_result(PatentSourceName.USPTO)]

    patent_adapter = AsyncMock()
    corpus_adapter = AsyncMock()
    llm_adapter = AsyncMock()
    patent_adapter.search = slow_patent_search
    corpus_adapter.search.return_value = [_make_patent_match(reference="US99999999")]
    llm_adapter.research.return_value = _make_research_finding()

    settings = EvidenceSettings(
        model_router_url="http://test-model-router",
        vector_router_url="http://test-vector-router",
        evidence_candidate_deadline_seconds=0.1,
    )
    scheduler = EvidenceScheduler(settings)
    service = TriangulationService(
        patent_adapter=patent_adapter,
        corpus_adapter=corpus_adapter,
        llm_adapter=llm_adapter,
        settings=settings,
        scheduler=scheduler,
    )

    bundle = await service.triangulate(CANDIDATE_ID, DOCUMENT_ID, CLAIM_TEXT, TECH_FIELD)

    assert bundle.source_flags[EvidenceSource.PatentApi] is False
    assert bundle.source_status[EvidenceSource.PatentApi] == SourceStatus.timeout
    assert bundle.patent_source_results == []


@pytest.mark.asyncio
async def test_patent_returns_within_deadline_with_ok_results_patent_api_true():
    uspto_result = PatentSourceResult(
        source=PatentSourceName.USPTO,
        outcome=PatentSourceOutcome.ok,
        hit_count=2,
        latency_ms=50.0,
    )
    epo_result = PatentSourceResult(
        source=PatentSourceName.EPO, outcome=PatentSourceOutcome.ok, hit_count=1, latency_ms=80.0
    )
    patent_adapter = AsyncMock()
    corpus_adapter = AsyncMock()
    llm_adapter = AsyncMock()
    patent_adapter.search.return_value = (
        [_make_patent_match("US11111111"), _make_patent_match("EP2222222")],
        [uspto_result, epo_result],
    )
    corpus_adapter.search.return_value = [_make_patent_match(reference="US99999999")]
    llm_adapter.research.return_value = _make_research_finding()

    settings = EvidenceSettings(
        model_router_url="http://test-model-router",
        vector_router_url="http://test-vector-router",
    )
    scheduler = EvidenceScheduler(settings)
    service = TriangulationService(
        patent_adapter=patent_adapter,
        corpus_adapter=corpus_adapter,
        llm_adapter=llm_adapter,
        settings=settings,
        scheduler=scheduler,
    )

    bundle = await service.triangulate(CANDIDATE_ID, DOCUMENT_ID, CLAIM_TEXT, TECH_FIELD)

    assert bundle.source_flags[EvidenceSource.PatentApi] is True
    assert bundle.source_status[EvidenceSource.PatentApi] != SourceStatus.timeout
    assert len(bundle.patent_source_results) == 2
    assert any(r.outcome == PatentSourceOutcome.ok for r in bundle.patent_source_results)
    assert bundle.degraded is False


@pytest.mark.asyncio
async def test_warning_enriched_with_source_statuses_and_patent_subsources(monkeypatch):
    import sentry_sdk

    captured_messages = []

    def mock_capture(message, level=None):
        captured_messages.append(message)

    monkeypatch.setattr(sentry_sdk, "capture_message", mock_capture)

    async def failing_corpus(*args, **kwargs):
        raise Exception("corpus unavailable")

    patent_adapter = AsyncMock()
    corpus_adapter = AsyncMock()
    llm_adapter = AsyncMock()
    uspto_result = PatentSourceResult(
        source=PatentSourceName.USPTO,
        outcome=PatentSourceOutcome.ok,
        hit_count=1,
        latency_ms=100.0,
    )
    epo_result = PatentSourceResult(
        source=PatentSourceName.EPO,
        outcome=PatentSourceOutcome.timeout,
        hit_count=0,
        latency_ms=15000.0,
    )
    patent_adapter.search.return_value = ([_make_patent_match()], [uspto_result, epo_result])
    corpus_adapter.search.side_effect = failing_corpus
    llm_adapter.research.return_value = _make_research_finding()

    settings = EvidenceSettings(
        model_router_url="http://test-model-router",
        vector_router_url="http://test-vector-router",
    )
    scheduler = EvidenceScheduler(settings)
    service = TriangulationService(
        patent_adapter=patent_adapter,
        corpus_adapter=corpus_adapter,
        llm_adapter=llm_adapter,
        settings=settings,
        scheduler=scheduler,
    )

    await service.triangulate(CANDIDATE_ID, DOCUMENT_ID, CLAIM_TEXT, TECH_FIELD)

    assert len(captured_messages) > 0
    message = captured_messages[0]
    assert "statuses=" in message
    assert "patent_subsources=" in message
    assert "USPTO" in message or "EPO" in message
