from datetime import UTC, datetime

import pytest

from scoring.domain.enums.confidence_band import ConfidenceBand
from scoring.domain.enums.evidence_source import EvidenceSource
from scoring.domain.models.evidence_bundle import EvidenceBundle
from scoring.domain.models.evidence_hit import EvidenceHit

MERGED_AT = datetime(2026, 1, 1, tzinfo=UTC)

PATENT_API_HIT = EvidenceHit(
    patent_id="US1234567",
    content_hash="hash_patent",
    title="Title PatentApi",
    citation="Citation PatentApi",
    url="https://patents.example.com/US1234567",
    similarity=0.9,
    sources={EvidenceSource.PatentApi},
)

SEED_CORPUS_HIT = EvidenceHit(
    patent_id=None,
    content_hash="hash_seed",
    title="Title SeedCorpus",
    citation="Citation SeedCorpus",
    url="https://corpus.example.com/seed_doc",
    similarity=0.8,
    sources={EvidenceSource.SeedCorpus},
)

LLM_RESEARCH_HIT = EvidenceHit(
    patent_id=None,
    content_hash="hash_llm",
    title="Title LlmResearch",
    citation="Citation LlmResearch",
    url="https://llm.example.com/research",
    similarity=0.7,
    sources={EvidenceSource.LlmResearch},
)


@pytest.fixture()
def bundle_all_sources() -> EvidenceBundle:
    return EvidenceBundle(
        id="bundle-001",
        batch_id="batch-001",
        job_id="job-001",
        candidate_id="cand-001",
        document_id="doc-001",
        hits=[PATENT_API_HIT, SEED_CORPUS_HIT, LLM_RESEARCH_HIT],
        confidence_band=ConfidenceBand.High,
        sources_used=[
            EvidenceSource.PatentApi,
            EvidenceSource.SeedCorpus,
            EvidenceSource.LlmResearch,
        ],
        source_flags={
            EvidenceSource.PatentApi: True,
            EvidenceSource.SeedCorpus: True,
            EvidenceSource.LlmResearch: True,
        },
        merged_at=MERGED_AT,
    )


@pytest.fixture()
def bundle_missing_one_source() -> EvidenceBundle:
    return EvidenceBundle(
        id="bundle-002",
        batch_id="batch-001",
        job_id="job-001",
        candidate_id="cand-001",
        document_id="doc-001",
        hits=[SEED_CORPUS_HIT, LLM_RESEARCH_HIT],
        confidence_band=ConfidenceBand.Medium,
        sources_used=[EvidenceSource.SeedCorpus, EvidenceSource.LlmResearch],
        source_flags={
            EvidenceSource.PatentApi: False,
            EvidenceSource.SeedCorpus: True,
            EvidenceSource.LlmResearch: True,
        },
        merged_at=MERGED_AT,
    )


@pytest.fixture()
def bundle_empty_hits() -> EvidenceBundle:
    return EvidenceBundle(
        id="bundle-003",
        batch_id="batch-001",
        job_id="job-001",
        candidate_id="cand-001",
        document_id="doc-001",
        hits=[],
        confidence_band=ConfidenceBand.Low,
        sources_used=[
            EvidenceSource.PatentApi,
            EvidenceSource.SeedCorpus,
            EvidenceSource.LlmResearch,
        ],
        source_flags={
            EvidenceSource.PatentApi: True,
            EvidenceSource.SeedCorpus: True,
            EvidenceSource.LlmResearch: True,
        },
        merged_at=MERGED_AT,
    )


@pytest.fixture()
def bundle_all_flags_false() -> EvidenceBundle:
    return EvidenceBundle(
        id="bundle-004",
        batch_id="batch-001",
        job_id="job-001",
        candidate_id="cand-001",
        document_id="doc-001",
        hits=[PATENT_API_HIT, SEED_CORPUS_HIT],
        confidence_band=ConfidenceBand.Low,
        sources_used=[],
        source_flags={
            EvidenceSource.PatentApi: False,
            EvidenceSource.SeedCorpus: False,
            EvidenceSource.LlmResearch: False,
        },
        merged_at=MERGED_AT,
    )


@pytest.fixture()
def bundle_multi_source_hit() -> EvidenceBundle:
    multi_hit = EvidenceHit(
        patent_id="US9999999",
        content_hash="hash_multi",
        title="Title Multi",
        citation="Citation Multi",
        url="https://example.com/multi",
        similarity=0.95,
        sources={EvidenceSource.PatentApi, EvidenceSource.SeedCorpus},
    )
    return EvidenceBundle(
        id="bundle-005",
        batch_id="batch-001",
        job_id="job-001",
        candidate_id="cand-001",
        document_id="doc-001",
        hits=[multi_hit],
        confidence_band=ConfidenceBand.High,
        sources_used=[EvidenceSource.PatentApi, EvidenceSource.SeedCorpus],
        source_flags={
            EvidenceSource.PatentApi: True,
            EvidenceSource.SeedCorpus: True,
            EvidenceSource.LlmResearch: False,
        },
        merged_at=MERGED_AT,
    )
