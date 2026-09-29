import pytest

from evidence.application.concurrency.evidence_scheduler import EvidenceScheduler
from evidence.application.triangulation_service import (
    TriangulationService,
    _evaluate_source_policy,
)
from evidence.domain.enums.evidence_source import EvidenceSource
from evidence.domain.errors.evidence_errors import EvidenceBundleBelowMinimumSourcesError
from evidence.domain.models.patent_match import PatentMatch
from evidence.domain.models.research_finding import Citation, ResearchFinding
from evidence.infrastructure.config.settings import EvidenceSettings


def _make_patent_match(
    reference: str = "US11234567",
    source: EvidenceSource = EvidenceSource.PatentApi,
) -> PatentMatch:
    return PatentMatch(
        reference=reference,
        title="Test Patent",
        applicant="Test Corp",
        date="2024-01-01",
        url="https://patents/test",
        relevance_score=0.9,
        source=source,
        abstract="",
        claims=[],
        jurisdiction="",
    )


def _make_research_finding(citations: list[str] | None = None) -> ResearchFinding:
    raw = citations if citations is not None else ["US99999999: test"]
    return ResearchFinding(
        findings=["test finding"],
        confidence=0.8,
        citations=[Citation(id=c, confidence=0.8) for c in raw],
    )


def _make_service(
    patent_result=None,
    corpus_result=None,
    llm_result=None,
    minimum_active_sources: int = 2,
    minimum_source_policy: str = "flag",
) -> TriangulationService:
    from unittest.mock import AsyncMock

    patent_adapter = AsyncMock()
    corpus_adapter = AsyncMock()
    llm_adapter = AsyncMock()

    if patent_result is not None:
        if isinstance(patent_result, BaseException):
            patent_adapter.search.side_effect = patent_result
        else:
            patent_adapter.search.return_value = (patent_result, [])

    if corpus_result is not None:
        if isinstance(corpus_result, BaseException):
            corpus_adapter.search.side_effect = corpus_result
        else:
            corpus_adapter.search.return_value = corpus_result

    if llm_result is not None:
        if isinstance(llm_result, BaseException):
            llm_adapter.research.side_effect = llm_result
        else:
            llm_adapter.research.return_value = llm_result

    settings = EvidenceSettings(
        model_router_url="http://test-model-router",
        vector_router_url="http://test-vector-router",
        minimum_active_sources=minimum_active_sources,
        minimum_source_policy=minimum_source_policy,
    )
    scheduler = EvidenceScheduler(settings)

    return TriangulationService(
        patent_adapter=patent_adapter,
        corpus_adapter=corpus_adapter,
        llm_adapter=llm_adapter,
        settings=settings,
        scheduler=scheduler,
    )


def test_evaluate_source_policy_all_three_sources():
    flags = {
        EvidenceSource.PatentApi: True,
        EvidenceSource.SeedCorpus: True,
        EvidenceSource.LlmResearch: True,
    }

    meets, active_count = _evaluate_source_policy(flags, minimum=2)

    assert active_count == 3
    assert meets is True


def test_evaluate_source_policy_two_sources():
    flags = {
        EvidenceSource.PatentApi: True,
        EvidenceSource.SeedCorpus: True,
        EvidenceSource.LlmResearch: False,
    }

    meets, active_count = _evaluate_source_policy(flags, minimum=2)

    assert active_count == 2
    assert meets is True


def test_evaluate_source_policy_one_source():
    flags = {
        EvidenceSource.PatentApi: True,
        EvidenceSource.SeedCorpus: False,
        EvidenceSource.LlmResearch: False,
    }

    meets, active_count = _evaluate_source_policy(flags, minimum=2)

    assert active_count == 1
    assert meets is False


def test_evaluate_source_policy_zero_sources():
    flags = {
        EvidenceSource.PatentApi: False,
        EvidenceSource.SeedCorpus: False,
        EvidenceSource.LlmResearch: False,
    }

    meets, active_count = _evaluate_source_policy(flags, minimum=2)

    assert active_count == 0
    assert meets is False


def test_evaluate_source_policy_minimum_zero():
    flags = {
        EvidenceSource.PatentApi: False,
        EvidenceSource.SeedCorpus: False,
        EvidenceSource.LlmResearch: False,
    }

    meets, active_count = _evaluate_source_policy(flags, minimum=0)

    assert active_count == 0
    assert meets is True


def test_evaluate_source_policy_minimum_one():
    flags = {
        EvidenceSource.PatentApi: True,
        EvidenceSource.SeedCorpus: False,
        EvidenceSource.LlmResearch: False,
    }

    meets, active_count = _evaluate_source_policy(flags, minimum=1)

    assert active_count == 1
    assert meets is True


def test_evaluate_source_policy_minimum_three():
    flags = {
        EvidenceSource.PatentApi: True,
        EvidenceSource.SeedCorpus: True,
        EvidenceSource.LlmResearch: False,
    }

    meets, active_count = _evaluate_source_policy(flags, minimum=3)

    assert active_count == 2
    assert meets is False


@pytest.mark.asyncio
async def test_triangulate_flag_mode_one_source_sets_meets_minimum_false():
    from evidence.domain.errors.evidence_errors import (
        CorpusSearchError,
        LlmResearchError,
    )

    patent_matches = [_make_patent_match()]
    service = _make_service(
        patent_result=patent_matches,
        corpus_result=CorpusSearchError("down", 503),
        llm_result=LlmResearchError("down", 503),
        minimum_active_sources=2,
        minimum_source_policy="flag",
    )

    bundle = await service.triangulate(
        candidate_id="cand-1",
        document_id="doc-1",
        claim_text="test claim",
        tech_field="test field",
    )

    assert bundle.active_source_count == 1
    assert bundle.meets_minimum_sources is False
    assert bundle.minimum_active_sources == 2


@pytest.mark.asyncio
async def test_triangulate_flag_mode_two_sources_sets_meets_minimum_true():
    from evidence.domain.errors.evidence_errors import LlmResearchError

    patent_matches = [_make_patent_match()]
    corpus_matches = [_make_patent_match(reference="US99999999", source=EvidenceSource.SeedCorpus)]
    service = _make_service(
        patent_result=patent_matches,
        corpus_result=corpus_matches,
        llm_result=LlmResearchError("down", 503),
        minimum_active_sources=2,
        minimum_source_policy="flag",
    )

    bundle = await service.triangulate(
        candidate_id="cand-1",
        document_id="doc-1",
        claim_text="test claim",
        tech_field="test field",
    )

    assert bundle.active_source_count == 2
    assert bundle.meets_minimum_sources is True
    assert bundle.minimum_active_sources == 2


@pytest.mark.asyncio
async def test_triangulate_flag_mode_three_sources_sets_meets_minimum_true():
    patent_matches = [_make_patent_match()]
    corpus_matches = [_make_patent_match(reference="US99999999", source=EvidenceSource.SeedCorpus)]
    llm_finding = _make_research_finding(citations=["US88888888: test"])
    service = _make_service(
        patent_result=patent_matches,
        corpus_result=corpus_matches,
        llm_result=llm_finding,
        minimum_active_sources=2,
        minimum_source_policy="flag",
    )

    bundle = await service.triangulate(
        candidate_id="cand-1",
        document_id="doc-1",
        claim_text="test claim",
        tech_field="test field",
    )

    assert bundle.active_source_count == 3
    assert bundle.meets_minimum_sources is True
    assert bundle.minimum_active_sources == 2


@pytest.mark.asyncio
async def test_triangulate_fail_mode_one_source_raises_below_minimum_error():
    from evidence.domain.errors.evidence_errors import (
        CorpusSearchError,
        LlmResearchError,
    )

    patent_matches = [_make_patent_match()]
    service = _make_service(
        patent_result=patent_matches,
        corpus_result=CorpusSearchError("down", 503),
        llm_result=LlmResearchError("down", 503),
        minimum_active_sources=2,
        minimum_source_policy="fail",
    )

    with pytest.raises(EvidenceBundleBelowMinimumSourcesError) as exc_info:
        await service.triangulate(
            candidate_id="cand-1",
            document_id="doc-1",
            claim_text="test claim",
            tech_field="test field",
        )

    assert "1/2 sources active" in str(exc_info.value)


@pytest.mark.asyncio
async def test_triangulate_fail_mode_two_sources_does_not_raise():
    from evidence.domain.errors.evidence_errors import LlmResearchError

    patent_matches = [_make_patent_match()]
    corpus_matches = [_make_patent_match(reference="US99999999", source=EvidenceSource.SeedCorpus)]
    service = _make_service(
        patent_result=patent_matches,
        corpus_result=corpus_matches,
        llm_result=LlmResearchError("down", 503),
        minimum_active_sources=2,
        minimum_source_policy="fail",
    )

    bundle = await service.triangulate(
        candidate_id="cand-1",
        document_id="doc-1",
        claim_text="test claim",
        tech_field="test field",
    )

    assert bundle.active_source_count == 2
    assert bundle.meets_minimum_sources is True


@pytest.mark.asyncio
async def test_triangulate_fail_mode_three_sources_does_not_raise():
    patent_matches = [_make_patent_match()]
    corpus_matches = [_make_patent_match(reference="US99999999", source=EvidenceSource.SeedCorpus)]
    llm_finding = _make_research_finding(citations=["US88888888: test"])
    service = _make_service(
        patent_result=patent_matches,
        corpus_result=corpus_matches,
        llm_result=llm_finding,
        minimum_active_sources=2,
        minimum_source_policy="fail",
    )

    bundle = await service.triangulate(
        candidate_id="cand-1",
        document_id="doc-1",
        claim_text="test claim",
        tech_field="test field",
    )

    assert bundle.active_source_count == 3
    assert bundle.meets_minimum_sources is True
