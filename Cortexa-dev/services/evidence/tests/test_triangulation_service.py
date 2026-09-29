import asyncio
import hashlib
import logging

import pytest

from evidence.application import triangulation_service as triangulation_service_module
from evidence.application.concurrency.evidence_scheduler import EvidenceScheduler
from evidence.application.triangulation_service import (
    TriangulationService,
    _confidence_band,
    _dedup_hits,
    _handle_llm_branch,
    _patent_match_to_hit,
    _research_finding_to_hits,
    _research_finding_to_summary,
    _sha256_of,
)
from evidence.domain.enums.confidence_band import ConfidenceBand
from evidence.domain.enums.evidence_source import EvidenceSource
from evidence.domain.enums.patent_source_name import PatentSourceName
from evidence.domain.enums.patent_source_outcome import PatentSourceOutcome
from evidence.domain.enums.source_status import SourceStatus
from evidence.domain.errors.evidence_errors import EvidenceBundleSourceUnavailableError
from evidence.domain.models.evidence_hit import EvidenceHit
from evidence.domain.models.patent_match import PatentMatch
from evidence.domain.models.patent_source_result import PatentSourceResult
from evidence.domain.models.research_finding import Citation, ResearchFinding
from evidence.infrastructure.config.settings import EvidenceSettings


def _ok_source_results() -> list[PatentSourceResult]:
    return [
        PatentSourceResult(
            source=PatentSourceName.USPTO,
            outcome=PatentSourceOutcome.ok,
            hit_count=5,
            latency_ms=120.0,
        ),
        PatentSourceResult(
            source=PatentSourceName.EPO,
            outcome=PatentSourceOutcome.ok,
            hit_count=3,
            latency_ms=200.0,
        ),
        PatentSourceResult(
            source=PatentSourceName.Lens,
            outcome=PatentSourceOutcome.ok,
            hit_count=4,
            latency_ms=80.0,
        ),
    ]


# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------

CANDIDATE_ID = "cand-001"
DOCUMENT_ID = "doc-001"
CLAIM_TEXT = "method for sparse tensor quantization"
TECH_FIELD = "machine learning"

PATENT_REF_A = "US11234567"
PATENT_TITLE_A = "Sparse Tensor Quantization Method"
PATENT_URL_A = "https://patents/US11234567"
RELEVANCE_A = 0.9

PATENT_REF_B = "EP3456789"
PATENT_TITLE_B = "Neural Compression via Pruning"
PATENT_URL_B = "https://patents/EP3456789"
RELEVANCE_B = 0.7

CITATION_A = "US11111111: related neural net compression"
CITATION_B = "US22222222: tensor decomposition"
LLM_CONFIDENCE = 0.82


# ---------------------------------------------------------------------------
# Factories
# ---------------------------------------------------------------------------


def _make_patent_match(
    reference: str = PATENT_REF_A,
    title: str = PATENT_TITLE_A,
    url: str = PATENT_URL_A,
    score: float = RELEVANCE_A,
    source: EvidenceSource = EvidenceSource.PatentApi,
    abstract: str = "",
    claims: list[str] | None = None,
    jurisdiction: str = "",
) -> PatentMatch:
    return PatentMatch(
        reference=reference,
        title=title,
        applicant="Acme Corp",
        date="2024-01-01",
        url=url,
        relevance_score=score,
        source=source,
        abstract=abstract,
        claims=claims or [],
        jurisdiction=jurisdiction,
    )


def _make_research_finding(
    citations: list[str] | None = None,
    confidence: float = LLM_CONFIDENCE,
) -> ResearchFinding:
    raw = citations if citations is not None else [CITATION_A, CITATION_B]
    return ResearchFinding(
        findings=["finding one"],
        confidence=confidence,
        citations=[Citation(id=c, confidence=confidence) for c in raw],
    )


def _make_hit(
    patent_id: str | None = PATENT_REF_A,
    content_hash: str = "abc123",
    title: str = PATENT_TITLE_A,
    similarity: float = 0.8,
    sources: set[EvidenceSource] | None = None,
) -> EvidenceHit:
    return EvidenceHit(
        patent_id=patent_id,
        content_hash=content_hash,
        title=title,
        citation=patent_id or title,
        url="",
        similarity=similarity,
        sources=sources if sources is not None else {EvidenceSource.PatentApi},
    )


def _make_service(
    patent_result=None,
    corpus_result=None,
    llm_result=None,
    patent_source_results=None,
) -> TriangulationService:
    from unittest.mock import AsyncMock

    patent_adapter = AsyncMock()
    corpus_adapter = AsyncMock()
    llm_adapter = AsyncMock()

    if patent_result is not None:
        if isinstance(patent_result, BaseException):
            patent_adapter.search.side_effect = patent_result
        else:
            source_results = (
                patent_source_results if patent_source_results is not None else _ok_source_results()
            )
            patent_adapter.search.return_value = (patent_result, source_results)

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
    )
    scheduler = EvidenceScheduler(settings)

    return TriangulationService(
        patent_adapter=patent_adapter,
        corpus_adapter=corpus_adapter,
        llm_adapter=llm_adapter,
        settings=settings,
        scheduler=scheduler,
    )


# ---------------------------------------------------------------------------
# _sha256_of
# ---------------------------------------------------------------------------


def test_sha256_of_same_text_same_hash():
    result_a = _sha256_of("hello world")
    result_b = _sha256_of("hello world")

    assert result_a == result_b


def test_sha256_of_different_text_different_hash():
    result_a = _sha256_of("hello world")
    result_b = _sha256_of("goodbye world")

    assert result_a != result_b


def test_sha256_of_lowercases_and_strips_before_hashing():
    expected = hashlib.sha256(b"hello world").hexdigest()

    assert _sha256_of("  Hello World  ") == expected
    assert _sha256_of("HELLO WORLD") == expected
    assert _sha256_of("hello world") == expected


# ---------------------------------------------------------------------------
# _confidence_band
# ---------------------------------------------------------------------------


def test_confidence_band_all_true_returns_high():
    flags = {
        EvidenceSource.PatentApi: True,
        EvidenceSource.SeedCorpus: True,
        EvidenceSource.LlmResearch: True,
    }

    assert _confidence_band(flags) == ConfidenceBand.High


def test_confidence_band_two_true_returns_medium():
    flags = {
        EvidenceSource.PatentApi: True,
        EvidenceSource.SeedCorpus: True,
        EvidenceSource.LlmResearch: False,
    }

    assert _confidence_band(flags) == ConfidenceBand.Medium


def test_confidence_band_one_true_returns_low():
    flags = {
        EvidenceSource.PatentApi: True,
        EvidenceSource.SeedCorpus: False,
        EvidenceSource.LlmResearch: False,
    }

    assert _confidence_band(flags) == ConfidenceBand.Low


def test_confidence_band_all_false_returns_low():
    flags = {
        EvidenceSource.PatentApi: False,
        EvidenceSource.SeedCorpus: False,
        EvidenceSource.LlmResearch: False,
    }

    assert _confidence_band(flags) == ConfidenceBand.Low


# ---------------------------------------------------------------------------
# _dedup_hits
# ---------------------------------------------------------------------------


def test_dedup_hits_no_duplicates_passthrough():
    hit_a = _make_hit(patent_id=PATENT_REF_A, content_hash="hash-a", similarity=0.9)
    hit_b = _make_hit(patent_id=PATENT_REF_B, content_hash="hash-b", similarity=0.7)

    result = _dedup_hits([hit_a, hit_b])

    assert len(result) == 2


def test_dedup_hits_same_patent_id_merges_sources_and_takes_max_similarity():
    hit_a = _make_hit(
        patent_id=PATENT_REF_A,
        content_hash="hash-a",
        similarity=0.6,
        sources={EvidenceSource.PatentApi},
    )
    hit_b = _make_hit(
        patent_id=PATENT_REF_A,
        content_hash="hash-b",
        similarity=0.9,
        sources={EvidenceSource.SeedCorpus},
    )

    result = _dedup_hits([hit_a, hit_b])

    assert len(result) == 1
    merged = result[0]
    assert merged.sources == {EvidenceSource.PatentApi, EvidenceSource.SeedCorpus}
    assert merged.similarity == 0.9


def test_dedup_hits_no_patent_id_same_content_hash_merges():
    shared_hash = _sha256_of("some citation text")
    hit_a = _make_hit(
        patent_id=None,
        content_hash=shared_hash,
        similarity=0.5,
        sources={EvidenceSource.LlmResearch},
    )
    hit_b = _make_hit(
        patent_id=None,
        content_hash=shared_hash,
        similarity=0.8,
        sources={EvidenceSource.SeedCorpus},
    )

    result = _dedup_hits([hit_a, hit_b])

    assert len(result) == 1
    assert result[0].sources == {EvidenceSource.LlmResearch, EvidenceSource.SeedCorpus}
    assert result[0].similarity == 0.8


def test_dedup_hits_patent_id_takes_priority_over_content_hash():
    shared_hash = _sha256_of("shared title")
    hit_with_id = _make_hit(
        patent_id=PATENT_REF_A,
        content_hash=shared_hash,
        similarity=0.6,
        sources={EvidenceSource.PatentApi},
    )
    hit_without_id = _make_hit(
        patent_id=None,
        content_hash=shared_hash,
        similarity=0.9,
        sources={EvidenceSource.LlmResearch},
    )

    result = _dedup_hits([hit_with_id, hit_without_id])

    assert len(result) == 2


# ---------------------------------------------------------------------------
# _patent_match_to_hit
# ---------------------------------------------------------------------------


def test_patent_match_to_hit_correct_field_mapping():
    match = _make_patent_match()

    hit = _patent_match_to_hit(match)

    assert hit.patent_id == match.reference
    assert hit.citation == match.reference
    assert hit.title == match.title
    assert hit.url == match.url
    assert hit.similarity == match.relevance_score
    assert hit.content_hash == _sha256_of(match.title)


def test_patent_match_to_hit_sources_contains_patent_api():
    match = _make_patent_match(source=EvidenceSource.PatentApi)

    hit = _patent_match_to_hit(match)

    assert EvidenceSource.PatentApi in hit.sources


def test_patent_match_to_hit_seed_corpus_source_preserved():
    match = _make_patent_match(source=EvidenceSource.SeedCorpus)

    hit = _patent_match_to_hit(match)

    assert EvidenceSource.SeedCorpus in hit.sources


def test_patent_match_to_hit_carries_abstract_claims_jurisdiction():
    match = _make_patent_match(
        abstract="A method for widget optimization",
        claims=["claim one", "claim two"],
        jurisdiction="US",
    )

    hit = _patent_match_to_hit(match)

    assert hit.abstract == "A method for widget optimization"
    assert hit.claims == ["claim one", "claim two"]
    assert hit.jurisdiction == "US"


# ---------------------------------------------------------------------------
# _research_finding_to_hits
# ---------------------------------------------------------------------------


def test_research_finding_to_hits_one_hit_per_citation():
    finding = _make_research_finding(citations=[CITATION_A, CITATION_B])

    hits = _research_finding_to_hits(finding)

    assert len(hits) == 2


def test_research_finding_to_hits_patent_id_is_none():
    finding = _make_research_finding(citations=[CITATION_A])

    hits = _research_finding_to_hits(finding)

    assert hits[0].patent_id is None


def test_research_finding_to_hits_sources_is_llm_research():
    finding = _make_research_finding(citations=[CITATION_A])

    hits = _research_finding_to_hits(finding)

    assert hits[0].sources == {EvidenceSource.LlmResearch}


def test_research_finding_to_hits_similarity_equals_confidence():
    finding = _make_research_finding(citations=[CITATION_A], confidence=0.75)

    hits = _research_finding_to_hits(finding)

    assert hits[0].similarity == 0.75


def test_research_finding_to_hits_similarity_is_per_citation():
    finding = ResearchFinding(
        findings=["finding one"],
        confidence=0.9,
        citations=[
            Citation(id=CITATION_A, confidence=0.31),
            Citation(id=CITATION_B, confidence=0.87),
        ],
    )

    hits = _research_finding_to_hits(finding)

    similarities = [h.similarity for h in hits]
    assert similarities == [0.31, 0.87]
    assert len(set(similarities)) == 2
    # The shared finding confidence must not leak into any citation similarity.
    assert all(s != finding.confidence for s in similarities)


def test_research_finding_to_hits_empty_citations_returns_empty():
    finding = _make_research_finding(citations=[])

    hits = _research_finding_to_hits(finding)

    assert hits == []


# ---------------------------------------------------------------------------
# _research_finding_to_summary / _handle_llm_branch (BUG183 Defect 2)
# ---------------------------------------------------------------------------


def test_research_finding_to_summary_carries_findings_and_confidence():
    finding = ResearchFinding(
        findings=["prior art does not disclose the claimed step", "novel over US11111111"],
        confidence=0.82,
        citations=[Citation(id=CITATION_A, confidence=0.82)],
    )

    summary = _research_finding_to_summary(finding)

    assert summary.findings == [
        "prior art does not disclose the claimed step",
        "novel over US11111111",
    ]
    assert summary.confidence == 0.82


def test_research_finding_to_summary_survives_zero_citations():
    finding = ResearchFinding(
        findings=["no prior art found; likely patentable"],
        confidence=0.91,
        citations=[],
    )

    summary = _research_finding_to_summary(finding)

    assert summary.findings == ["no prior art found; likely patentable"]
    assert summary.confidence == 0.91


def test_handle_llm_branch_success_returns_hits_and_summary():
    finding = _make_research_finding(citations=[CITATION_A, CITATION_B])

    ok, status, hits, summary = _handle_llm_branch(finding)

    assert ok is True
    assert status == SourceStatus.active
    assert len(hits) == 2
    assert summary is not None
    assert summary.findings == finding.findings
    assert summary.confidence == finding.confidence


def test_handle_llm_branch_no_citations_still_returns_summary():
    finding = ResearchFinding(
        findings=["no prior art found; likely patentable"],
        confidence=0.9,
        citations=[],
    )

    ok, status, hits, summary = _handle_llm_branch(finding)

    assert ok is True
    assert status == SourceStatus.empty
    assert hits == []
    assert summary is not None
    assert summary.findings == ["no prior art found; likely patentable"]
    assert summary.confidence == 0.9


def test_handle_llm_branch_exception_returns_no_summary():
    from evidence.domain.errors.evidence_errors import LlmResearchError

    ok, status, hits, summary = _handle_llm_branch(LlmResearchError("down", 503))

    assert ok is False
    assert status == SourceStatus.error
    assert hits == []
    assert summary is None


# ---------------------------------------------------------------------------
# TriangulationService.triangulate integration tests
# ---------------------------------------------------------------------------


@pytest.mark.asyncio
async def test_triangulate_all_sources_return_results_high_confidence():
    patent_matches = [_make_patent_match(reference=PATENT_REF_A)]
    corpus_matches = [_make_patent_match(reference=PATENT_REF_B, source=EvidenceSource.SeedCorpus)]
    llm_finding = _make_research_finding(citations=[CITATION_A])

    service = _make_service(
        patent_result=patent_matches,
        corpus_result=corpus_matches,
        llm_result=llm_finding,
    )

    bundle = await service.triangulate(CANDIDATE_ID, DOCUMENT_ID, CLAIM_TEXT, TECH_FIELD)

    assert bundle.confidence_band == ConfidenceBand.High
    assert bundle.source_flags[EvidenceSource.PatentApi] is True
    assert bundle.source_flags[EvidenceSource.SeedCorpus] is True
    assert bundle.source_flags[EvidenceSource.LlmResearch] is True
    assert len(bundle.hits) == 3


@pytest.mark.asyncio
async def test_triangulate_patent_api_fails_flag_false_no_exception():
    from evidence.domain.errors.evidence_errors import PatentApiError

    corpus_matches = [_make_patent_match(reference=PATENT_REF_B, source=EvidenceSource.SeedCorpus)]
    llm_finding = _make_research_finding(citations=[CITATION_A])

    service = _make_service(
        patent_result=PatentApiError("USPTO unreachable", 503),
        corpus_result=corpus_matches,
        llm_result=llm_finding,
    )

    bundle = await service.triangulate(CANDIDATE_ID, DOCUMENT_ID, CLAIM_TEXT, TECH_FIELD)

    assert bundle.source_flags[EvidenceSource.PatentApi] is False
    assert bundle.confidence_band in {ConfidenceBand.Medium, ConfidenceBand.Low}


@pytest.mark.asyncio
async def test_triangulate_corpus_fails_flag_false_no_exception():
    from evidence.domain.errors.evidence_errors import CorpusSearchError

    patent_matches = [_make_patent_match()]
    llm_finding = _make_research_finding(citations=[CITATION_A])

    service = _make_service(
        patent_result=patent_matches,
        corpus_result=CorpusSearchError("vector-router down", 503),
        llm_result=llm_finding,
    )

    bundle = await service.triangulate(CANDIDATE_ID, DOCUMENT_ID, CLAIM_TEXT, TECH_FIELD)

    assert bundle.source_flags[EvidenceSource.SeedCorpus] is False
    assert bundle.confidence_band in {ConfidenceBand.Medium, ConfidenceBand.Low}


@pytest.mark.asyncio
async def test_triangulate_llm_fails_flag_false_no_exception():
    from evidence.domain.errors.evidence_errors import LlmResearchError

    patent_matches = [_make_patent_match()]
    corpus_matches = [_make_patent_match(reference=PATENT_REF_B, source=EvidenceSource.SeedCorpus)]

    service = _make_service(
        patent_result=patent_matches,
        corpus_result=corpus_matches,
        llm_result=LlmResearchError("model-router timeout", 504),
    )

    bundle = await service.triangulate(CANDIDATE_ID, DOCUMENT_ID, CLAIM_TEXT, TECH_FIELD)

    assert bundle.source_flags[EvidenceSource.LlmResearch] is False
    assert bundle.confidence_band in {ConfidenceBand.Medium, ConfidenceBand.Low}


@pytest.mark.asyncio
async def test_triangulate_all_sources_fail_raises_unavailable_error():
    from evidence.domain.errors.evidence_errors import (
        CorpusSearchError,
        LlmResearchError,
        PatentApiError,
    )

    service = _make_service(
        patent_result=PatentApiError("down"),
        corpus_result=CorpusSearchError("down"),
        llm_result=LlmResearchError("down"),
    )

    with pytest.raises(EvidenceBundleSourceUnavailableError):
        await service.triangulate(CANDIDATE_ID, DOCUMENT_ID, CLAIM_TEXT, TECH_FIELD)


@pytest.mark.asyncio
async def test_triangulate_same_patent_ref_from_patent_api_and_corpus_deduped():
    patent_matches = [
        _make_patent_match(reference=PATENT_REF_A, score=0.8, source=EvidenceSource.PatentApi)
    ]
    corpus_matches = [
        _make_patent_match(reference=PATENT_REF_A, score=0.9, source=EvidenceSource.SeedCorpus)
    ]
    llm_finding = _make_research_finding(citations=[])

    service = _make_service(
        patent_result=patent_matches,
        corpus_result=corpus_matches,
        llm_result=llm_finding,
    )

    bundle = await service.triangulate(CANDIDATE_ID, DOCUMENT_ID, CLAIM_TEXT, TECH_FIELD)

    patent_hits = [h for h in bundle.hits if h.patent_id == PATENT_REF_A]
    assert len(patent_hits) == 1
    merged_hit = patent_hits[0]
    assert EvidenceSource.PatentApi in merged_hit.sources
    assert EvidenceSource.SeedCorpus in merged_hit.sources
    assert merged_hit.similarity == 0.9


@pytest.mark.asyncio
async def test_triangulate_cancelled_error_from_patent_adapter_propagates():
    from unittest.mock import AsyncMock

    patent_adapter = AsyncMock()
    corpus_adapter = AsyncMock()
    llm_adapter = AsyncMock()

    patent_adapter.search.side_effect = asyncio.CancelledError()
    corpus_adapter.search.return_value = [_make_patent_match()]
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

    with pytest.raises(asyncio.CancelledError):
        await service.triangulate(CANDIDATE_ID, DOCUMENT_ID, CLAIM_TEXT, TECH_FIELD)


@pytest.mark.asyncio
async def test_triangulate_cancelled_error_from_corpus_adapter_propagates():
    from unittest.mock import AsyncMock

    patent_adapter = AsyncMock()
    corpus_adapter = AsyncMock()
    llm_adapter = AsyncMock()

    patent_adapter.search.return_value = ([_make_patent_match()], [])
    corpus_adapter.search.side_effect = asyncio.CancelledError()
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

    with pytest.raises(asyncio.CancelledError):
        await service.triangulate(CANDIDATE_ID, DOCUMENT_ID, CLAIM_TEXT, TECH_FIELD)


@pytest.mark.asyncio
async def test_triangulate_cancelled_error_from_llm_adapter_propagates():
    from unittest.mock import AsyncMock

    patent_adapter = AsyncMock()
    corpus_adapter = AsyncMock()
    llm_adapter = AsyncMock()

    patent_adapter.search.return_value = ([_make_patent_match()], [])
    corpus_adapter.search.return_value = [
        _make_patent_match(reference=PATENT_REF_B, source=EvidenceSource.SeedCorpus)
    ]
    llm_adapter.research.side_effect = asyncio.CancelledError()

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

    with pytest.raises(asyncio.CancelledError):
        await service.triangulate(CANDIDATE_ID, DOCUMENT_ID, CLAIM_TEXT, TECH_FIELD)


@pytest.mark.asyncio
async def test_triangulate_finding_with_citations_bundle_has_hits_and_llm_research():
    patent_matches = [_make_patent_match()]
    corpus_matches = [_make_patent_match(reference=PATENT_REF_B, source=EvidenceSource.SeedCorpus)]
    llm_finding = ResearchFinding(
        findings=["claim reads on independent claim 1 of US11111111"],
        confidence=0.77,
        citations=[Citation(id=CITATION_A, confidence=0.77)],
    )

    service = _make_service(
        patent_result=patent_matches,
        corpus_result=corpus_matches,
        llm_result=llm_finding,
    )

    bundle = await service.triangulate(CANDIDATE_ID, DOCUMENT_ID, CLAIM_TEXT, TECH_FIELD)

    llm_hits = [h for h in bundle.hits if EvidenceSource.LlmResearch in h.sources]
    assert len(llm_hits) == 1
    assert bundle.llm_research is not None
    assert bundle.llm_research.findings == ["claim reads on independent claim 1 of US11111111"]
    assert bundle.llm_research.confidence == 0.77


@pytest.mark.asyncio
async def test_triangulate_finding_with_reasoning_no_citations_llm_research_still_persisted():
    patent_matches = [_make_patent_match()]
    corpus_matches = [_make_patent_match(reference=PATENT_REF_B, source=EvidenceSource.SeedCorpus)]
    llm_finding = ResearchFinding(
        findings=["no prior art found; candidate appears patentable"],
        confidence=0.88,
        citations=[],
    )

    service = _make_service(
        patent_result=patent_matches,
        corpus_result=corpus_matches,
        llm_result=llm_finding,
    )

    bundle = await service.triangulate(CANDIDATE_ID, DOCUMENT_ID, CLAIM_TEXT, TECH_FIELD)

    llm_hits = [h for h in bundle.hits if EvidenceSource.LlmResearch in h.sources]
    assert llm_hits == []
    assert bundle.source_flags[EvidenceSource.LlmResearch] is True
    assert bundle.llm_research is not None
    assert bundle.llm_research.findings == ["no prior art found; candidate appears patentable"]
    assert bundle.llm_research.confidence == 0.88


@pytest.mark.asyncio
async def test_triangulate_llm_source_fails_bundle_llm_research_is_none():
    from evidence.domain.errors.evidence_errors import LlmResearchError

    patent_matches = [_make_patent_match()]
    corpus_matches = [_make_patent_match(reference=PATENT_REF_B, source=EvidenceSource.SeedCorpus)]

    service = _make_service(
        patent_result=patent_matches,
        corpus_result=corpus_matches,
        llm_result=LlmResearchError("model-router timeout", 504),
    )

    bundle = await service.triangulate(CANDIDATE_ID, DOCUMENT_ID, CLAIM_TEXT, TECH_FIELD)

    assert bundle.llm_research is None


@pytest.mark.asyncio
async def test_triangulate_bundle_fields_populated_correctly():
    patent_matches = [_make_patent_match()]
    corpus_matches = [_make_patent_match(reference=PATENT_REF_B, source=EvidenceSource.SeedCorpus)]
    llm_finding = _make_research_finding(citations=[CITATION_A])

    service = _make_service(
        patent_result=patent_matches,
        corpus_result=corpus_matches,
        llm_result=llm_finding,
    )

    bundle = await service.triangulate(CANDIDATE_ID, DOCUMENT_ID, CLAIM_TEXT, TECH_FIELD)

    assert bundle.candidate_id == CANDIDATE_ID
    assert bundle.document_id == DOCUMENT_ID
    assert bundle.id != ""
    assert bundle.merged_at is not None
    assert EvidenceSource.PatentApi in bundle.sources_used
    assert EvidenceSource.SeedCorpus in bundle.sources_used
    assert EvidenceSource.LlmResearch in bundle.sources_used


@pytest.mark.asyncio
async def test_triangulate_all_patent_sources_failed_marks_bundle_degraded():
    from evidence.domain.enums.patent_source_name import PatentSourceName
    from evidence.domain.enums.patent_source_outcome import PatentSourceOutcome
    from evidence.domain.models.patent_source_result import PatentSourceResult

    failed_source_results = [
        PatentSourceResult(
            source=PatentSourceName.USPTO,
            outcome=PatentSourceOutcome.timeout,
            hit_count=0,
            latency_ms=15000.0,
        ),
        PatentSourceResult(
            source=PatentSourceName.EPO,
            outcome=PatentSourceOutcome.auth_error,
            hit_count=0,
            latency_ms=200.0,
        ),
        PatentSourceResult(
            source=PatentSourceName.Lens,
            outcome=PatentSourceOutcome.rate_limited,
            hit_count=0,
            latency_ms=50.0,
        ),
    ]

    corpus_matches = [_make_patent_match(reference=PATENT_REF_B, source=EvidenceSource.SeedCorpus)]
    llm_finding = _make_research_finding(citations=[CITATION_A])

    service = _make_service(
        patent_result=[],
        patent_source_results=failed_source_results,
        corpus_result=corpus_matches,
        llm_result=llm_finding,
    )

    bundle = await service.triangulate(CANDIDATE_ID, DOCUMENT_ID, CLAIM_TEXT, TECH_FIELD)

    assert bundle.degraded is True
    assert len(bundle.degraded_sources) == 3
    assert PatentSourceName.USPTO in bundle.degraded_sources
    assert PatentSourceName.EPO in bundle.degraded_sources
    assert PatentSourceName.Lens in bundle.degraded_sources


@pytest.mark.asyncio
async def test_triangulate_all_patent_sources_ok_bundle_not_degraded():
    patent_matches = [_make_patent_match()]
    corpus_matches = [_make_patent_match(reference=PATENT_REF_B, source=EvidenceSource.SeedCorpus)]
    llm_finding = _make_research_finding(citations=[CITATION_A])

    service = _make_service(
        patent_result=patent_matches,
        patent_source_results=_ok_source_results(),
        corpus_result=corpus_matches,
        llm_result=llm_finding,
    )

    bundle = await service.triangulate(CANDIDATE_ID, DOCUMENT_ID, CLAIM_TEXT, TECH_FIELD)

    assert bundle.degraded is False
    assert bundle.degraded_sources == []
    assert len(bundle.patent_source_results) == 3
    assert all(r.outcome == PatentSourceOutcome.ok for r in bundle.patent_source_results)


# ---------------------------------------------------------------------------
# Degraded-triangulation observability signal (< 3 active sources)
# ---------------------------------------------------------------------------


@pytest.mark.asyncio
async def test_triangulate_two_active_sources_emits_degraded_warning(caplog, monkeypatch):
    from evidence.domain.errors.evidence_errors import CorpusSearchError

    captured: list[str] = []
    monkeypatch.setattr(
        triangulation_service_module.sentry_sdk,
        "capture_message",
        lambda message, level="info": captured.append(message),
    )

    patent_matches = [_make_patent_match()]
    llm_finding = _make_research_finding(citations=[CITATION_A])

    service = _make_service(
        patent_result=patent_matches,
        corpus_result=CorpusSearchError("vector-router down", 503),
        llm_result=llm_finding,
    )

    with caplog.at_level(logging.WARNING, logger="evidence.application.triangulation_service"):
        bundle = await service.triangulate(
            CANDIDATE_ID, DOCUMENT_ID, CLAIM_TEXT, TECH_FIELD, batch_id="batch-1", job_id="job-1"
        )

    assert bundle.source_flags[EvidenceSource.SeedCorpus] is False
    degraded_records = [r for r in caplog.records if "triangulation degraded" in r.message]
    assert len(degraded_records) == 1
    assert CANDIDATE_ID in degraded_records[0].message
    assert DOCUMENT_ID in degraded_records[0].message
    assert "batch-1" in degraded_records[0].message
    assert "job-1" in degraded_records[0].message
    assert len(captured) == 1
    assert "triangulation degraded" in captured[0]


@pytest.mark.asyncio
async def test_triangulate_all_three_sources_active_no_degraded_warning(caplog, monkeypatch):
    captured: list[str] = []
    monkeypatch.setattr(
        triangulation_service_module.sentry_sdk,
        "capture_message",
        lambda message, level="info": captured.append(message),
    )

    patent_matches = [_make_patent_match()]
    corpus_matches = [_make_patent_match(reference=PATENT_REF_B, source=EvidenceSource.SeedCorpus)]
    llm_finding = _make_research_finding(citations=[CITATION_A])

    service = _make_service(
        patent_result=patent_matches,
        corpus_result=corpus_matches,
        llm_result=llm_finding,
    )

    with caplog.at_level(logging.WARNING, logger="evidence.application.triangulation_service"):
        await service.triangulate(CANDIDATE_ID, DOCUMENT_ID, CLAIM_TEXT, TECH_FIELD)

    degraded_records = [r for r in caplog.records if "triangulation degraded" in r.message]
    assert degraded_records == []
    assert captured == []


# ---------------------------------------------------------------------------
# US110: Permanent LlmResearch re-raise and soft-degrade preservation
# ---------------------------------------------------------------------------


@pytest.mark.asyncio
async def test_triangulate_llm_research_permanent_400_re_raised():
    from evidence.domain.errors.evidence_errors import LlmResearchError

    patent_matches = [_make_patent_match()]
    corpus_matches = [_make_patent_match(reference=PATENT_REF_B, source=EvidenceSource.SeedCorpus)]

    service = _make_service(
        patent_result=patent_matches,
        corpus_result=corpus_matches,
        llm_result=LlmResearchError("Bad request", status_code=400),
    )

    with pytest.raises(LlmResearchError) as exc_info:
        await service.triangulate(CANDIDATE_ID, DOCUMENT_ID, CLAIM_TEXT, TECH_FIELD)

    assert exc_info.value.status_code == 400


@pytest.mark.asyncio
async def test_triangulate_llm_research_permanent_422_re_raised():
    from evidence.domain.errors.evidence_errors import LlmResearchError

    patent_matches = [_make_patent_match()]
    corpus_matches = [_make_patent_match(reference=PATENT_REF_B, source=EvidenceSource.SeedCorpus)]

    service = _make_service(
        patent_result=patent_matches,
        corpus_result=corpus_matches,
        llm_result=LlmResearchError("Unprocessable entity", status_code=422),
    )

    with pytest.raises(LlmResearchError) as exc_info:
        await service.triangulate(CANDIDATE_ID, DOCUMENT_ID, CLAIM_TEXT, TECH_FIELD)

    assert exc_info.value.status_code == 422


@pytest.mark.asyncio
async def test_triangulate_llm_research_transient_503_degrades_to_two_sources():
    from evidence.domain.errors.evidence_errors import LlmResearchError

    patent_matches = [_make_patent_match()]
    corpus_matches = [_make_patent_match(reference=PATENT_REF_B, source=EvidenceSource.SeedCorpus)]

    service = _make_service(
        patent_result=patent_matches,
        corpus_result=corpus_matches,
        llm_result=LlmResearchError("Service unavailable", status_code=503),
    )

    bundle = await service.triangulate(CANDIDATE_ID, DOCUMENT_ID, CLAIM_TEXT, TECH_FIELD)

    assert bundle.source_flags[EvidenceSource.LlmResearch] is False
    assert bundle.source_flags[EvidenceSource.PatentApi] is True
    assert bundle.source_flags[EvidenceSource.SeedCorpus] is True
    assert bundle.active_source_count == 2


@pytest.mark.asyncio
async def test_triangulate_llm_research_transient_no_status_code_degrades():
    from evidence.domain.errors.evidence_errors import LlmResearchError

    patent_matches = [_make_patent_match()]
    corpus_matches = [_make_patent_match(reference=PATENT_REF_B, source=EvidenceSource.SeedCorpus)]

    service = _make_service(
        patent_result=patent_matches,
        corpus_result=corpus_matches,
        llm_result=LlmResearchError("Network timeout", status_code=None),
    )

    bundle = await service.triangulate(CANDIDATE_ID, DOCUMENT_ID, CLAIM_TEXT, TECH_FIELD)

    assert bundle.source_flags[EvidenceSource.LlmResearch] is False
    assert bundle.active_source_count == 2


@pytest.mark.asyncio
async def test_triangulate_patent_api_permanent_400_degrades_no_re_raise():
    from evidence.domain.errors.evidence_errors import PatentApiError

    corpus_matches = [_make_patent_match(reference=PATENT_REF_B, source=EvidenceSource.SeedCorpus)]
    llm_finding = _make_research_finding(citations=[CITATION_A])

    service = _make_service(
        patent_result=PatentApiError("Bad request", status_code=400),
        corpus_result=corpus_matches,
        llm_result=llm_finding,
    )

    bundle = await service.triangulate(CANDIDATE_ID, DOCUMENT_ID, CLAIM_TEXT, TECH_FIELD)

    assert bundle.source_flags[EvidenceSource.PatentApi] is False
    assert bundle.source_flags[EvidenceSource.SeedCorpus] is True
    assert bundle.source_flags[EvidenceSource.LlmResearch] is True
    assert bundle.active_source_count == 2


@pytest.mark.asyncio
async def test_triangulate_patent_api_permanent_403_degrades_no_re_raise():
    from evidence.domain.errors.evidence_errors import PatentApiError

    corpus_matches = [_make_patent_match(reference=PATENT_REF_B, source=EvidenceSource.SeedCorpus)]
    llm_finding = _make_research_finding(citations=[CITATION_A])

    service = _make_service(
        patent_result=PatentApiError("Forbidden", status_code=403),
        corpus_result=corpus_matches,
        llm_result=llm_finding,
    )

    bundle = await service.triangulate(CANDIDATE_ID, DOCUMENT_ID, CLAIM_TEXT, TECH_FIELD)

    assert bundle.source_flags[EvidenceSource.PatentApi] is False
    assert bundle.active_source_count == 2


@pytest.mark.asyncio
async def test_triangulate_corpus_search_permanent_422_degrades_no_re_raise():
    from evidence.domain.errors.evidence_errors import CorpusSearchError

    patent_matches = [_make_patent_match()]
    llm_finding = _make_research_finding(citations=[CITATION_A])

    service = _make_service(
        patent_result=patent_matches,
        corpus_result=CorpusSearchError("Unprocessable query", status_code=422),
        llm_result=llm_finding,
    )

    bundle = await service.triangulate(CANDIDATE_ID, DOCUMENT_ID, CLAIM_TEXT, TECH_FIELD)

    assert bundle.source_flags[EvidenceSource.SeedCorpus] is False
    assert bundle.source_flags[EvidenceSource.PatentApi] is True
    assert bundle.source_flags[EvidenceSource.LlmResearch] is True
    assert bundle.active_source_count == 2


@pytest.mark.asyncio
async def test_triangulate_corpus_search_no_status_code_degrades_no_re_raise():
    from evidence.domain.errors.evidence_errors import CorpusSearchError

    patent_matches = [_make_patent_match()]
    llm_finding = _make_research_finding(citations=[CITATION_A])

    service = _make_service(
        patent_result=patent_matches,
        corpus_result=CorpusSearchError("Connection timeout"),
        llm_result=llm_finding,
    )

    bundle = await service.triangulate(CANDIDATE_ID, DOCUMENT_ID, CLAIM_TEXT, TECH_FIELD)

    assert bundle.source_flags[EvidenceSource.SeedCorpus] is False
    assert bundle.active_source_count == 2


@pytest.mark.asyncio
async def test_triangulate_ai_model_passed_to_llm_adapter():
    EXPECTED_PRIMARY = "gpt-5.5"

    from unittest.mock import AsyncMock

    patent_adapter = AsyncMock()
    corpus_adapter = AsyncMock()
    llm_adapter = AsyncMock()

    patent_adapter.search.return_value = ([_make_patent_match()], _ok_source_results())
    corpus_adapter.search.return_value = [
        _make_patent_match(reference=PATENT_REF_B, source=EvidenceSource.SeedCorpus)
    ]
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

    await service.triangulate(
        CANDIDATE_ID,
        DOCUMENT_ID,
        CLAIM_TEXT,
        TECH_FIELD,
        ai_model=EXPECTED_PRIMARY,
    )

    llm_adapter.research.assert_awaited_once()
    call_kwargs = llm_adapter.research.call_args.kwargs
    assert call_kwargs["model"] == EXPECTED_PRIMARY
    assert "secondary_model" not in call_kwargs


@pytest.mark.asyncio
async def test_triangulate_llm_research_model_override_takes_precedence_over_ai_model():
    BATCH_AI_MODEL = "gpt-5.5"
    OVERRIDE_MODEL = "gpt-5.4"

    from unittest.mock import AsyncMock

    patent_adapter = AsyncMock()
    corpus_adapter = AsyncMock()
    llm_adapter = AsyncMock()

    patent_adapter.search.return_value = ([_make_patent_match()], _ok_source_results())
    corpus_adapter.search.return_value = [
        _make_patent_match(reference=PATENT_REF_B, source=EvidenceSource.SeedCorpus)
    ]
    llm_adapter.research.return_value = _make_research_finding()

    settings = EvidenceSettings(
        model_router_url="http://test-model-router",
        vector_router_url="http://test-vector-router",
        llm_research_model=OVERRIDE_MODEL,
    )
    scheduler = EvidenceScheduler(settings)

    service = TriangulationService(
        patent_adapter=patent_adapter,
        corpus_adapter=corpus_adapter,
        llm_adapter=llm_adapter,
        settings=settings,
        scheduler=scheduler,
    )

    await service.triangulate(
        CANDIDATE_ID,
        DOCUMENT_ID,
        CLAIM_TEXT,
        TECH_FIELD,
        ai_model=BATCH_AI_MODEL,
    )

    llm_adapter.research.assert_awaited_once()
    call_kwargs = llm_adapter.research.call_args.kwargs
    assert call_kwargs["model"] == OVERRIDE_MODEL


@pytest.mark.asyncio
async def test_triangulate_no_llm_research_model_override_falls_back_to_ai_model():
    BATCH_AI_MODEL = "gpt-5.5"

    from unittest.mock import AsyncMock

    patent_adapter = AsyncMock()
    corpus_adapter = AsyncMock()
    llm_adapter = AsyncMock()

    patent_adapter.search.return_value = ([_make_patent_match()], _ok_source_results())
    corpus_adapter.search.return_value = [
        _make_patent_match(reference=PATENT_REF_B, source=EvidenceSource.SeedCorpus)
    ]
    llm_adapter.research.return_value = _make_research_finding()

    settings = EvidenceSettings(
        model_router_url="http://test-model-router",
        vector_router_url="http://test-vector-router",
        llm_research_model=None,
    )
    scheduler = EvidenceScheduler(settings)

    service = TriangulationService(
        patent_adapter=patent_adapter,
        corpus_adapter=corpus_adapter,
        llm_adapter=llm_adapter,
        settings=settings,
        scheduler=scheduler,
    )

    await service.triangulate(
        CANDIDATE_ID,
        DOCUMENT_ID,
        CLAIM_TEXT,
        TECH_FIELD,
        ai_model=BATCH_AI_MODEL,
    )

    llm_adapter.research.assert_awaited_once()
    call_kwargs = llm_adapter.research.call_args.kwargs
    assert call_kwargs["model"] == BATCH_AI_MODEL


@pytest.mark.asyncio
async def test_triangulate_patent_api_fails_siblings_complete_degraded_bundle():
    from evidence.domain.errors.evidence_errors import PatentApiError

    corpus_matches = [_make_patent_match(reference=PATENT_REF_B, source=EvidenceSource.SeedCorpus)]
    llm_finding = _make_research_finding(citations=[CITATION_A])

    service = _make_service(
        patent_result=PatentApiError("patent api down", 503),
        corpus_result=corpus_matches,
        llm_result=llm_finding,
    )

    bundle = await service.triangulate(CANDIDATE_ID, DOCUMENT_ID, CLAIM_TEXT, TECH_FIELD)

    assert bundle.source_flags[EvidenceSource.PatentApi] is False
    assert bundle.source_flags[EvidenceSource.SeedCorpus] is True
    assert bundle.source_flags[EvidenceSource.LlmResearch] is True
    assert bundle.active_source_count == 2


@pytest.mark.asyncio
async def test_triangulate_corpus_fails_siblings_complete_degraded_bundle():
    from evidence.domain.errors.evidence_errors import CorpusSearchError

    patent_matches = [_make_patent_match()]
    llm_finding = _make_research_finding(citations=[CITATION_A])

    service = _make_service(
        patent_result=patent_matches,
        corpus_result=CorpusSearchError("vector router down", 503),
        llm_result=llm_finding,
    )

    bundle = await service.triangulate(CANDIDATE_ID, DOCUMENT_ID, CLAIM_TEXT, TECH_FIELD)

    assert bundle.source_flags[EvidenceSource.PatentApi] is True
    assert bundle.source_flags[EvidenceSource.SeedCorpus] is False
    assert bundle.source_flags[EvidenceSource.LlmResearch] is True
    assert bundle.active_source_count == 2


@pytest.mark.asyncio
async def test_triangulate_llm_transient_fails_siblings_complete_degraded_bundle():
    from evidence.domain.errors.evidence_errors import LlmResearchError

    patent_matches = [_make_patent_match()]
    corpus_matches = [_make_patent_match(reference=PATENT_REF_B, source=EvidenceSource.SeedCorpus)]

    service = _make_service(
        patent_result=patent_matches,
        corpus_result=corpus_matches,
        llm_result=LlmResearchError("model router timeout", 504),
    )

    bundle = await service.triangulate(CANDIDATE_ID, DOCUMENT_ID, CLAIM_TEXT, TECH_FIELD)

    assert bundle.source_flags[EvidenceSource.PatentApi] is True
    assert bundle.source_flags[EvidenceSource.SeedCorpus] is True
    assert bundle.source_flags[EvidenceSource.LlmResearch] is False
    assert bundle.active_source_count == 2


@pytest.mark.asyncio
async def test_triangulate_deadline_exceeded_below_minimum_raises_deadline_error():
    from unittest.mock import AsyncMock

    from evidence.domain.errors.evidence_errors import EvidenceCandidateDeadlineExceededError

    patent_adapter = AsyncMock()
    corpus_adapter = AsyncMock()
    llm_adapter = AsyncMock()

    async def slow_search(*args, **kwargs):
        await asyncio.sleep(2.0)
        return ([_make_patent_match()], _ok_source_results())

    async def slow_corpus_search(*args, **kwargs):
        await asyncio.sleep(2.0)
        return [_make_patent_match(reference=PATENT_REF_B, source=EvidenceSource.SeedCorpus)]

    patent_adapter.search.side_effect = slow_search
    corpus_adapter.search.side_effect = slow_corpus_search
    llm_adapter.research.return_value = _make_research_finding()

    settings = EvidenceSettings(
        model_router_url="http://test-model-router",
        vector_router_url="http://test-vector-router",
        evidence_candidate_deadline_seconds=0.2,
        minimum_active_sources=2,
    )
    scheduler = EvidenceScheduler(settings)

    service = TriangulationService(
        patent_adapter=patent_adapter,
        corpus_adapter=corpus_adapter,
        llm_adapter=llm_adapter,
        settings=settings,
        scheduler=scheduler,
    )

    with pytest.raises(EvidenceCandidateDeadlineExceededError) as exc_info:
        await service.triangulate(CANDIDATE_ID, DOCUMENT_ID, CLAIM_TEXT, TECH_FIELD)

    assert set(exc_info.value.pending_sources) == {"PatentApi", "SeedCorpus"}
    assert exc_info.value.elapsed_seconds >= 0.2
    assert str(exc_info.value) != ""


@pytest.mark.asyncio
async def test_triangulate_deadline_exceeded_meets_minimum_returns_degraded_bundle():
    from unittest.mock import AsyncMock

    patent_adapter = AsyncMock()
    corpus_adapter = AsyncMock()
    llm_adapter = AsyncMock()

    async def slow_llm_research(*args, **kwargs):
        await asyncio.sleep(2.0)
        return _make_research_finding()

    patent_adapter.search.return_value = ([_make_patent_match()], _ok_source_results())
    corpus_adapter.search.return_value = [
        _make_patent_match(reference=PATENT_REF_B, source=EvidenceSource.SeedCorpus)
    ]
    llm_adapter.research.side_effect = slow_llm_research

    settings = EvidenceSettings(
        model_router_url="http://test-model-router",
        vector_router_url="http://test-vector-router",
        evidence_candidate_deadline_seconds=0.2,
        minimum_active_sources=2,
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
    assert bundle.source_flags[EvidenceSource.SeedCorpus] is True
    assert bundle.source_flags[EvidenceSource.LlmResearch] is False
    assert bundle.active_source_count == 2


@pytest.mark.asyncio
async def test_triangulate_min_live_patent_sources_honored_after_degraded_gather():
    from evidence.domain.enums.patent_source_name import PatentSourceName
    from evidence.domain.enums.patent_source_outcome import PatentSourceOutcome
    from evidence.domain.models.patent_source_result import PatentSourceResult

    failed_source_results = [
        PatentSourceResult(
            source=PatentSourceName.USPTO,
            outcome=PatentSourceOutcome.timeout,
            hit_count=0,
            latency_ms=15000.0,
        ),
        PatentSourceResult(
            source=PatentSourceName.EPO,
            outcome=PatentSourceOutcome.auth_error,
            hit_count=0,
            latency_ms=200.0,
        ),
        PatentSourceResult(
            source=PatentSourceName.Lens,
            outcome=PatentSourceOutcome.ok,
            hit_count=2,
            latency_ms=50.0,
        ),
    ]

    corpus_matches = [_make_patent_match(reference=PATENT_REF_B, source=EvidenceSource.SeedCorpus)]
    llm_finding = _make_research_finding(citations=[CITATION_A])

    service = _make_service(
        patent_result=[_make_patent_match()],
        patent_source_results=failed_source_results,
        corpus_result=corpus_matches,
        llm_result=llm_finding,
    )

    bundle = await service.triangulate(CANDIDATE_ID, DOCUMENT_ID, CLAIM_TEXT, TECH_FIELD)

    assert bundle.degraded is True
    assert PatentSourceName.USPTO in bundle.degraded_sources
    assert PatentSourceName.EPO in bundle.degraded_sources


# ---------------------------------------------------------------------------
# BUG169: content-filtered LLM source tracked honestly via source_status
# ---------------------------------------------------------------------------


@pytest.mark.asyncio
async def test_triangulate_llm_content_filtered_degrades_to_two_of_three_not_reraised():
    from evidence.domain.errors.evidence_errors import LlmResearchError

    patent_matches = [_make_patent_match()]
    corpus_matches = [_make_patent_match(reference=PATENT_REF_B, source=EvidenceSource.SeedCorpus)]

    service = _make_service(
        patent_result=patent_matches,
        corpus_result=corpus_matches,
        llm_result=LlmResearchError(
            "Response blocked by content filter", status_code=400, content_filter=True
        ),
    )

    bundle = await service.triangulate(CANDIDATE_ID, DOCUMENT_ID, CLAIM_TEXT, TECH_FIELD)

    assert bundle.source_flags[EvidenceSource.LlmResearch] is False
    assert bundle.source_status[EvidenceSource.LlmResearch] == SourceStatus.filtered
    assert bundle.source_status[EvidenceSource.PatentApi] == SourceStatus.active
    assert bundle.source_status[EvidenceSource.SeedCorpus] == SourceStatus.active
    assert bundle.active_source_count == 2
    assert bundle.confidence_band == ConfidenceBand.Medium


@pytest.mark.asyncio
async def test_triangulate_llm_content_filtered_400_not_reraised_even_though_permanent_code():
    from evidence.domain.errors.evidence_errors import LlmResearchError

    patent_matches = [_make_patent_match()]
    corpus_matches = [_make_patent_match(reference=PATENT_REF_B, source=EvidenceSource.SeedCorpus)]

    service = _make_service(
        patent_result=patent_matches,
        corpus_result=corpus_matches,
        llm_result=LlmResearchError("Content filtered", status_code=422, content_filter=True),
    )

    bundle = await service.triangulate(CANDIDATE_ID, DOCUMENT_ID, CLAIM_TEXT, TECH_FIELD)

    assert bundle.source_status[EvidenceSource.LlmResearch] == SourceStatus.filtered
    assert bundle.active_source_count == 2


@pytest.mark.asyncio
async def test_triangulate_llm_permanent_401_without_content_filter_still_reraises():
    from evidence.domain.errors.evidence_errors import LlmResearchError

    patent_matches = [_make_patent_match()]
    corpus_matches = [_make_patent_match(reference=PATENT_REF_B, source=EvidenceSource.SeedCorpus)]

    service = _make_service(
        patent_result=patent_matches,
        corpus_result=corpus_matches,
        llm_result=LlmResearchError("Unauthorized", status_code=401),
    )

    with pytest.raises(LlmResearchError) as exc_info:
        await service.triangulate(CANDIDATE_ID, DOCUMENT_ID, CLAIM_TEXT, TECH_FIELD)

    assert exc_info.value.status_code == 401
    assert exc_info.value.content_filter is False


@pytest.mark.asyncio
async def test_triangulate_llm_permanent_400_without_content_filter_still_reraises():
    from evidence.domain.errors.evidence_errors import LlmResearchError

    patent_matches = [_make_patent_match()]
    corpus_matches = [_make_patent_match(reference=PATENT_REF_B, source=EvidenceSource.SeedCorpus)]

    service = _make_service(
        patent_result=patent_matches,
        corpus_result=corpus_matches,
        llm_result=LlmResearchError("Bad request", status_code=400, content_filter=False),
    )

    with pytest.raises(LlmResearchError) as exc_info:
        await service.triangulate(CANDIDATE_ID, DOCUMENT_ID, CLAIM_TEXT, TECH_FIELD)

    assert exc_info.value.status_code == 400


@pytest.mark.asyncio
async def test_triangulate_genuine_zero_hits_llm_status_empty_flag_unchanged():
    llm_finding_no_citations = _make_research_finding(citations=[])
    patent_matches = [_make_patent_match()]
    corpus_matches = [_make_patent_match(reference=PATENT_REF_B, source=EvidenceSource.SeedCorpus)]

    service = _make_service(
        patent_result=patent_matches,
        corpus_result=corpus_matches,
        llm_result=llm_finding_no_citations,
    )

    bundle = await service.triangulate(CANDIDATE_ID, DOCUMENT_ID, CLAIM_TEXT, TECH_FIELD)

    assert bundle.source_flags[EvidenceSource.LlmResearch] is True
    assert bundle.source_status[EvidenceSource.LlmResearch] == SourceStatus.empty
    assert bundle.confidence_band == ConfidenceBand.High
    assert bundle.active_source_count == 3


@pytest.mark.asyncio
async def test_triangulate_genuine_zero_hits_corpus_status_empty_flag_unchanged():
    patent_matches = [_make_patent_match()]
    llm_finding = _make_research_finding(citations=[CITATION_A])

    service = _make_service(
        patent_result=patent_matches,
        corpus_result=[],
        llm_result=llm_finding,
    )

    bundle = await service.triangulate(CANDIDATE_ID, DOCUMENT_ID, CLAIM_TEXT, TECH_FIELD)

    assert bundle.source_flags[EvidenceSource.SeedCorpus] is True
    assert bundle.source_status[EvidenceSource.SeedCorpus] == SourceStatus.empty
    assert bundle.active_source_count == 3


@pytest.mark.asyncio
async def test_triangulate_all_active_sources_status_all_active():
    patent_matches = [_make_patent_match()]
    corpus_matches = [_make_patent_match(reference=PATENT_REF_B, source=EvidenceSource.SeedCorpus)]
    llm_finding = _make_research_finding(citations=[CITATION_A])

    service = _make_service(
        patent_result=patent_matches,
        corpus_result=corpus_matches,
        llm_result=llm_finding,
    )

    bundle = await service.triangulate(CANDIDATE_ID, DOCUMENT_ID, CLAIM_TEXT, TECH_FIELD)

    assert bundle.source_status[EvidenceSource.PatentApi] == SourceStatus.active
    assert bundle.source_status[EvidenceSource.SeedCorpus] == SourceStatus.active
    assert bundle.source_status[EvidenceSource.LlmResearch] == SourceStatus.active


@pytest.mark.asyncio
async def test_triangulate_patent_api_transient_error_status_error():
    from evidence.domain.errors.evidence_errors import PatentApiError

    corpus_matches = [_make_patent_match(reference=PATENT_REF_B, source=EvidenceSource.SeedCorpus)]
    llm_finding = _make_research_finding(citations=[CITATION_A])

    service = _make_service(
        patent_result=PatentApiError("USPTO unreachable", 503),
        corpus_result=corpus_matches,
        llm_result=llm_finding,
    )

    bundle = await service.triangulate(CANDIDATE_ID, DOCUMENT_ID, CLAIM_TEXT, TECH_FIELD)

    assert bundle.source_status[EvidenceSource.PatentApi] == SourceStatus.error


@pytest.mark.asyncio
async def test_triangulate_deadline_exceeded_pending_source_status_timeout():
    from unittest.mock import AsyncMock

    patent_adapter = AsyncMock()
    corpus_adapter = AsyncMock()
    llm_adapter = AsyncMock()

    async def slow_llm_research(*args, **kwargs):
        await asyncio.sleep(2.0)
        return _make_research_finding()

    patent_adapter.search.return_value = ([_make_patent_match()], _ok_source_results())
    corpus_adapter.search.return_value = [
        _make_patent_match(reference=PATENT_REF_B, source=EvidenceSource.SeedCorpus)
    ]
    llm_adapter.research.side_effect = slow_llm_research

    settings = EvidenceSettings(
        model_router_url="http://test-model-router",
        vector_router_url="http://test-vector-router",
        evidence_candidate_deadline_seconds=0.2,
        minimum_active_sources=2,
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

    assert bundle.source_status[EvidenceSource.LlmResearch] == SourceStatus.timeout
