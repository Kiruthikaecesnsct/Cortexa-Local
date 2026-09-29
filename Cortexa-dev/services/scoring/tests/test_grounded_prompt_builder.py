from datetime import UTC, datetime

import pytest

from scoring.application.prompt.grounded_prompt_builder import (
    HitRenderConfig,
    build_grounded_prompt,
)
from scoring.domain.enums.confidence_band import ConfidenceBand
from scoring.domain.enums.evidence_source import EvidenceSource
from scoring.domain.errors.scoring_errors import UngroundedVerdictError
from scoring.domain.models.evidence_bundle import EvidenceBundle
from scoring.domain.models.evidence_hit import EvidenceHit

MERGED_AT = datetime(2026, 1, 1, tzinfo=UTC)
CANDIDATE = "A method for optimizing neural network inference on edge devices."


def _make_bundle(hit: EvidenceHit) -> EvidenceBundle:
    return EvidenceBundle(
        id="bundle-enrichment",
        batch_id="batch-001",
        job_id="job-001",
        candidate_id="cand-001",
        document_id="doc-001",
        hits=[hit],
        confidence_band=ConfidenceBand.High,
        sources_used=[EvidenceSource.PatentApi],
        source_flags={
            EvidenceSource.PatentApi: True,
            EvidenceSource.SeedCorpus: False,
            EvidenceSource.LlmResearch: False,
        },
        merged_at=MERGED_AT,
    )


def _make_hit(**overrides: object) -> EvidenceHit:
    base = dict(
        patent_id="US1234567B2",
        content_hash="abcd1234",
        title="Widget fastening mechanism",
        citation="Smith 2019",
        url="https://example.com/US1234567B2",
        similarity=0.9,
        sources={EvidenceSource.PatentApi},
    )
    base.update(overrides)
    return EvidenceHit(**base)


# ---------------------------------------------------------------------------
# AC1 — All three source section labels appear in the prompt
# ---------------------------------------------------------------------------


def test_build_grounded_prompt_all_sources_includes_patent_api_section(bundle_all_sources):
    result = build_grounded_prompt(CANDIDATE, bundle_all_sources)

    assert "== PATENT API EVIDENCE" in result


def test_build_grounded_prompt_all_sources_includes_seed_corpus_section(bundle_all_sources):
    result = build_grounded_prompt(CANDIDATE, bundle_all_sources)

    assert "== SEED CORPUS EVIDENCE" in result


def test_build_grounded_prompt_all_sources_includes_llm_research_section(bundle_all_sources):
    result = build_grounded_prompt(CANDIDATE, bundle_all_sources)

    assert "== LLM RESEARCH EVIDENCE" in result


# ---------------------------------------------------------------------------
# AC2 — Source status labels (available / unavailable / fallback)
# ---------------------------------------------------------------------------


def test_build_grounded_prompt_all_flags_true_renders_all_available(bundle_all_sources):
    result = build_grounded_prompt(CANDIDATE, bundle_all_sources)

    assert "== PATENT API EVIDENCE [status: available] ==" in result
    assert "== SEED CORPUS EVIDENCE [status: available] ==" in result
    assert "== LLM RESEARCH EVIDENCE [status: available] ==" in result


def test_build_grounded_prompt_missing_source_flag_false_renders_unavailable(
    bundle_missing_one_source,
):
    result = build_grounded_prompt(CANDIDATE, bundle_missing_one_source)

    assert "== PATENT API EVIDENCE [status: unavailable] ==" in result


def test_build_grounded_prompt_missing_source_available_sources_remain_available(
    bundle_missing_one_source,
):
    result = build_grounded_prompt(CANDIDATE, bundle_missing_one_source)

    assert "== SEED CORPUS EVIDENCE [status: available] ==" in result
    assert "== LLM RESEARCH EVIDENCE [status: available] ==" in result


def test_build_grounded_prompt_flag_true_not_in_sources_used_renders_fallback():
    hit = EvidenceHit(
        patent_id=None,
        content_hash="hash_patentonly",
        title="Patent Only Title",
        citation="Patent Only Citation",
        url="https://example.com/patent_only",
        similarity=0.85,
        sources={EvidenceSource.PatentApi},
    )
    bundle = EvidenceBundle(
        id="bundle-fallback",
        batch_id="batch-001",
        job_id="job-001",
        candidate_id="cand-001",
        document_id="doc-001",
        hits=[hit],
        confidence_band=ConfidenceBand.Medium,
        sources_used=[EvidenceSource.PatentApi],
        source_flags={
            EvidenceSource.PatentApi: True,
            EvidenceSource.SeedCorpus: True,
            EvidenceSource.LlmResearch: False,
        },
        merged_at=MERGED_AT,
    )

    result = build_grounded_prompt(CANDIDATE, bundle)

    assert "== SEED CORPUS EVIDENCE [status: fallback] ==" in result


# ---------------------------------------------------------------------------
# AC3 — Citations include provenance (url, hash, and ref)
# ---------------------------------------------------------------------------


def test_build_grounded_prompt_hit_lines_contain_url_and_hash(bundle_all_sources):
    result = build_grounded_prompt(CANDIDATE, bundle_all_sources)

    lines = result.splitlines()
    hit_lines = [ln for ln in lines if ln.startswith("[E")]
    assert len(hit_lines) == 3
    for line in hit_lines:
        assert "url:" in line
        assert "hash:" in line


def test_build_grounded_prompt_hit_lines_contain_evidence_ref(bundle_all_sources):
    result = build_grounded_prompt(CANDIDATE, bundle_all_sources)

    lines = result.splitlines()
    hit_lines = [ln for ln in lines if ln.startswith("[E")]
    refs = {ln.split("]")[0].lstrip("[") for ln in hit_lines}
    assert refs == {"E1", "E2", "E3"}


# ---------------------------------------------------------------------------
# AC4 — Bundle missing all grounding raises UngroundedVerdictError
# ---------------------------------------------------------------------------


def test_build_grounded_prompt_empty_hits_raises_ungrounded_verdict_error(bundle_empty_hits):
    with pytest.raises(UngroundedVerdictError) as exc_info:
        build_grounded_prompt(CANDIDATE, bundle_empty_hits)

    assert exc_info.value.code == "UNGROUNDED_VERDICT"


def test_build_grounded_prompt_all_flags_false_raises_ungrounded_verdict_error(
    bundle_all_flags_false,
):
    with pytest.raises(UngroundedVerdictError) as exc_info:
        build_grounded_prompt(CANDIDATE, bundle_all_flags_false)

    assert exc_info.value.code == "UNGROUNDED_VERDICT"


# ---------------------------------------------------------------------------
# AC5 — Deterministic output regardless of hit input order
# ---------------------------------------------------------------------------


def test_build_grounded_prompt_deterministic_regardless_of_hit_order():
    hit_a = EvidenceHit(
        patent_id="US0000001",
        content_hash="hash_aaa",
        title="Title A",
        citation="Citation A",
        url="https://example.com/a",
        similarity=0.9,
        sources={EvidenceSource.PatentApi},
    )
    hit_b = EvidenceHit(
        patent_id="US0000002",
        content_hash="hash_bbb",
        title="Title B",
        citation="Citation B",
        url="https://example.com/b",
        similarity=0.8,
        sources={EvidenceSource.SeedCorpus},
    )

    def make_bundle(hits):
        return EvidenceBundle(
            id="bundle-det",
            batch_id="batch-001",
            job_id="job-001",
            candidate_id="cand-001",
            document_id="doc-001",
            hits=hits,
            confidence_band=ConfidenceBand.High,
            sources_used=[EvidenceSource.PatentApi, EvidenceSource.SeedCorpus],
            source_flags={
                EvidenceSource.PatentApi: True,
                EvidenceSource.SeedCorpus: True,
                EvidenceSource.LlmResearch: False,
            },
            merged_at=MERGED_AT,
        )

    bundle_forward = make_bundle([hit_a, hit_b])
    bundle_reversed = make_bundle([hit_b, hit_a])

    result_forward = build_grounded_prompt(CANDIDATE, bundle_forward)
    result_reversed = build_grounded_prompt(CANDIDATE, bundle_reversed)

    assert result_forward == result_reversed


# ---------------------------------------------------------------------------
# Multi-source hit appears in multiple sections
# ---------------------------------------------------------------------------


def test_build_grounded_prompt_multi_source_hit_appears_in_patent_api_section(
    bundle_multi_source_hit,
):
    result = build_grounded_prompt(CANDIDATE, bundle_multi_source_hit)

    patent_section_start = result.index("== PATENT API EVIDENCE")
    seed_section_start = result.index("== SEED CORPUS EVIDENCE")
    patent_section = result[patent_section_start:seed_section_start]
    assert "[E1]" in patent_section


def test_build_grounded_prompt_multi_source_hit_appears_in_seed_corpus_section(
    bundle_multi_source_hit,
):
    result = build_grounded_prompt(CANDIDATE, bundle_multi_source_hit)

    seed_section_start = result.index("== SEED CORPUS EVIDENCE")
    llm_section_start = result.index("== LLM RESEARCH EVIDENCE")
    seed_section = result[seed_section_start:llm_section_start]
    assert "[E1]" in seed_section


def test_build_grounded_prompt_multi_source_hit_uses_same_ref_in_both_sections(
    bundle_multi_source_hit,
):
    result = build_grounded_prompt(CANDIDATE, bundle_multi_source_hit)

    lines = result.splitlines()
    hit_lines = [ln for ln in lines if ln.startswith("[E1]")]
    assert len(hit_lines) == 2
    assert hit_lines[0] == hit_lines[1]


# ---------------------------------------------------------------------------
# Patent ID rendering
# ---------------------------------------------------------------------------


def test_build_grounded_prompt_hit_with_patent_id_renders_patent_prefix():
    hit = EvidenceHit(
        patent_id="US1234567",
        content_hash="hash_with_patent",
        title="Titled Hit",
        citation="Some Citation",
        url="https://example.com/p",
        similarity=0.9,
        sources={EvidenceSource.PatentApi},
    )
    bundle = EvidenceBundle(
        id="bundle-pid",
        batch_id="batch-001",
        job_id="job-001",
        candidate_id="cand-001",
        document_id="doc-001",
        hits=[hit],
        confidence_band=ConfidenceBand.High,
        sources_used=[EvidenceSource.PatentApi],
        source_flags={
            EvidenceSource.PatentApi: True,
            EvidenceSource.SeedCorpus: False,
            EvidenceSource.LlmResearch: False,
        },
        merged_at=MERGED_AT,
    )

    result = build_grounded_prompt(CANDIDATE, bundle)

    hit_line = next(ln for ln in result.splitlines() if ln.startswith("[E1]"))
    assert hit_line.startswith("[E1] patent:US1234567 |")


def test_build_grounded_prompt_hit_without_patent_id_omits_patent_prefix():
    hit = EvidenceHit(
        patent_id=None,
        content_hash="hash_no_patent",
        title="No Patent Title",
        citation="No Patent Citation",
        url="https://example.com/np",
        similarity=0.75,
        sources={EvidenceSource.LlmResearch},
    )
    bundle = EvidenceBundle(
        id="bundle-nopid",
        batch_id="batch-001",
        job_id="job-001",
        candidate_id="cand-001",
        document_id="doc-001",
        hits=[hit],
        confidence_band=ConfidenceBand.Medium,
        sources_used=[EvidenceSource.LlmResearch],
        source_flags={
            EvidenceSource.PatentApi: False,
            EvidenceSource.SeedCorpus: False,
            EvidenceSource.LlmResearch: True,
        },
        merged_at=MERGED_AT,
    )

    result = build_grounded_prompt(CANDIDATE, bundle)

    hit_line = next(ln for ln in result.splitlines() if ln.startswith("[E1]"))
    assert "patent:" not in hit_line


# ---------------------------------------------------------------------------
# US105 — abstract/claim enrichment
# ---------------------------------------------------------------------------


def test_render_hit_with_abstract_and_claim_includes_both_lines_in_order():
    hit = _make_hit(abstract="A device for fastening widgets.", claims=["A widget fastener."])
    bundle = _make_bundle(hit)

    result = build_grounded_prompt(CANDIDATE, bundle)
    lines = result.splitlines()
    head_idx = next(i for i, ln in enumerate(lines) if ln.startswith("[E1]"))

    assert lines[head_idx + 1] == "      abstract: A device for fastening widgets."
    assert lines[head_idx + 2] == "      claim 1: A widget fastener."


def test_render_hit_empty_abstract_non_empty_claim_omits_abstract_line():
    hit = _make_hit(abstract="", claims=["A widget fastener."])
    bundle = _make_bundle(hit)

    result = build_grounded_prompt(CANDIDATE, bundle)

    assert "abstract:" not in result
    assert "      claim 1: A widget fastener." in result


def test_render_hit_empty_claims_non_empty_abstract_omits_claim_lines():
    hit = _make_hit(abstract="A device for fastening widgets.", claims=[])
    bundle = _make_bundle(hit)

    result = build_grounded_prompt(CANDIDATE, bundle)

    assert "      abstract: A device for fastening widgets." in result
    assert "claim " not in result


def test_render_hit_both_empty_matches_pre_us105_single_line_format():
    hit = _make_hit(abstract="", claims=[])
    bundle = _make_bundle(hit)

    result = build_grounded_prompt(CANDIDATE, bundle)
    hit_line = next(ln for ln in result.splitlines() if ln.startswith("[E1]"))

    assert hit_line == (
        "[E1] patent:US1234567B2 | Widget fastening mechanism | Smith 2019 "
        "| url: https://example.com/US1234567B2 | hash: abcd1234"
    )
    assert "abstract:" not in result
    assert "claim " not in result


def test_render_hit_long_claim_truncated_at_whitespace_boundary():
    long_claim = "word " * 400
    hit = _make_hit(abstract="", claims=[long_claim])
    bundle = _make_bundle(hit)
    cfg = HitRenderConfig(max_claim_chars_per_hit=700)

    result = build_grounded_prompt(CANDIDATE, bundle, cfg)
    claim_line = next(ln for ln in result.splitlines() if ln.startswith("      claim 1:"))
    rendered = claim_line[len("      claim 1: ") :]

    assert rendered.endswith("…[truncated]")
    body = rendered[: -len("…[truncated]")]
    assert len(body) <= 700
    assert not body.endswith(" ")


def test_render_hit_double_equals_replaced_with_double_dash_pre_and_post_truncation():
    hit = _make_hit(
        abstract="A==B device",
        claims=[("C==D claim text " * 100)],
    )
    bundle = _make_bundle(hit)
    cfg = HitRenderConfig(max_claim_chars_per_hit=50)

    result = build_grounded_prompt(CANDIDATE, bundle, cfg)
    hit_lines = [ln for ln in result.splitlines() if ln.startswith("[E") or ln.startswith("     ")]
    hit_block = "\n".join(hit_lines)

    assert "A--B device" in hit_block
    assert "==" not in hit_block
    assert "C--D" in hit_block


def test_render_hit_claims_per_hit_limits_to_first_claim_only():
    hit = _make_hit(
        abstract="", claims=["claim one", "claim two", "claim three", "claim four", "claim five"]
    )
    bundle = _make_bundle(hit)
    cfg = HitRenderConfig(claims_per_hit=1)

    result = build_grounded_prompt(CANDIDATE, bundle, cfg)

    assert "      claim 1: claim one" in result
    assert "claim 2:" not in result
    assert "claim two" not in result


def test_render_hit_whitespace_only_claim_is_skipped():
    hit = _make_hit(abstract="", claims=["   "])
    bundle = _make_bundle(hit)

    result = build_grounded_prompt(CANDIDATE, bundle)

    assert "claim 1:" not in result
