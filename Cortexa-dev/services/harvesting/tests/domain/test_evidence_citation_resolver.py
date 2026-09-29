import pytest

from harvesting.domain.enums.scoring_axis import ScoringAxis
from harvesting.domain.models.axis_score import AxisScore
from harvesting.domain.services.evidence_citation_resolver import (
    resolve_citations,
    resolve_evidence_source_views,
    resolve_provenance_links,
    resolve_source_availability,
    resolve_source_status,
)

HIT_LOWEST_PATENT_ID = "US1000000A"
HIT_MIDDLE_PATENT_ID = "US2000000A"
HIT_HIGHEST_PATENT_ID = "US3000000A"


def _hit(patent_id: str, content_hash: str, title: str, source: str) -> dict:
    return {
        "patent_id": patent_id,
        "content_hash": content_hash,
        "title": title,
        "url": f"https://example.com/patent/{patent_id}",
        "similarity": 0.5,
        "sources": [source],
    }


def _bundle() -> dict:
    # Hits are intentionally listed out of patent_id order to prove the
    # resolver re-sorts them exactly like scoring's grounded_prompt_builder /
    # five_axis_parser before mapping E{i} refs.
    return {
        "hits": [
            _hit(HIT_HIGHEST_PATENT_ID, "hash-a", "Patent A", "PatentApi"),
            _hit(HIT_LOWEST_PATENT_ID, "hash-b", "Patent B", "SeedCorpus"),
            _hit(HIT_MIDDLE_PATENT_ID, "hash-c", "Patent C", "LlmResearch"),
        ]
    }


def _axis(axis: ScoringAxis, refs: list[str]) -> AxisScore:
    return AxisScore(axis=axis, score=50, refs=refs)


def test_resolve_citations_maps_ref_to_sorted_hit_by_patent_id_then_content_hash():
    axes = [_axis(ScoringAxis.Novelty, ["E1", "E2", "E3"])]

    citations = resolve_citations(axes, _bundle())

    assert [c.patent_id for c in citations] == [
        HIT_LOWEST_PATENT_ID,
        HIT_MIDDLE_PATENT_ID,
        HIT_HIGHEST_PATENT_ID,
    ]


def test_resolve_citations_maps_source_type_using_priority_order():
    axes = [_axis(ScoringAxis.Novelty, ["E1", "E2", "E3"])]

    citations = resolve_citations(axes, _bundle())

    assert citations[0].source_type == "vector_corpus"
    assert citations[1].source_type == "llm_deep_research"
    assert citations[2].source_type == "patent_api"


def test_resolve_citations_dedupes_same_ref_cited_across_multiple_axes():
    axes = [
        _axis(ScoringAxis.Novelty, ["E1"]),
        _axis(ScoringAxis.Inventiveness, ["E1"]),
    ]

    citations = resolve_citations(axes, _bundle())

    assert len(citations) == 1
    assert citations[0].ref == "E1"


def test_resolve_citations_preserves_first_seen_order_across_axes():
    axes = [
        _axis(ScoringAxis.Novelty, ["E3"]),
        _axis(ScoringAxis.Inventiveness, ["E1"]),
    ]

    citations = resolve_citations(axes, _bundle())

    assert [c.ref for c in citations] == ["E3", "E1"]


def test_resolve_citations_returns_empty_list_when_bundle_is_none():
    axes = [_axis(ScoringAxis.Novelty, ["E1"])]

    citations = resolve_citations(axes, None)

    assert citations == []


def test_resolve_citations_returns_empty_list_when_bundle_has_no_hits():
    axes = [_axis(ScoringAxis.Novelty, ["E1"])]

    citations = resolve_citations(axes, {"hits": []})

    assert citations == []


def test_resolve_citations_skips_out_of_range_ref_from_stale_bundle():
    axes = [_axis(ScoringAxis.Novelty, ["E1", "E99"])]

    citations = resolve_citations(axes, _bundle())

    assert [c.ref for c in citations] == ["E1"]


def test_resolve_citations_skips_malformed_ref():
    axes = [_axis(ScoringAxis.Novelty, ["not-a-ref"])]

    citations = resolve_citations(axes, _bundle())

    assert citations == []


def test_resolve_provenance_links_builds_from_document_id_and_source_span():
    candidate = {
        "document_id": "doc-001",
        "source_span": {"locator": "chunk-004", "source_kind": "extraction"},
    }

    links = resolve_provenance_links(candidate)

    assert len(links) == 1
    assert links[0].document_id == "doc-001"
    assert links[0].locator == "chunk-004"
    assert links[0].source_kind == "extraction"


def test_resolve_provenance_links_returns_empty_when_neither_document_id_nor_source_span():
    candidate = {}

    links = resolve_provenance_links(candidate)

    assert links == []


def test_resolve_provenance_links_builds_from_document_id_alone():
    candidate = {"document_id": "doc-002"}

    links = resolve_provenance_links(candidate)

    assert len(links) == 1
    assert links[0].document_id == "doc-002"
    assert links[0].locator == ""


def test_resolve_provenance_links_carries_full_source_span():
    candidate = {
        "document_id": "doc-003",
        "source_chunk_index": 5,
        "source_span": {
            "locator": "chars:100-200",
            "source_kind": "extraction",
            "page_number": 12,
            "section_hint": "Introduction",
            "span_start": 100,
            "span_end": 200,
            "excerpt": "This is the extracted text.",
        },
    }

    links = resolve_provenance_links(candidate)

    assert len(links) == 1
    link = links[0]
    assert link.document_id == "doc-003"
    assert link.locator == "chars:100-200"
    assert link.source_kind == "extraction"
    assert link.chunk_id == "doc-003|5"
    assert link.source_chunk_index == 5
    assert link.page_number == 12
    assert link.section_hint == "Introduction"
    assert link.span_start == 100
    assert link.span_end == 200
    assert link.excerpt == "This is the extracted text."


def test_resolve_provenance_links_minimal_source_span_defaults_new_fields_to_none():
    candidate = {
        "document_id": "doc-004",
        "source_span": {
            "locator": "chars:50-60",
            "source_kind": "extraction",
        },
    }

    links = resolve_provenance_links(candidate)

    assert len(links) == 1
    link = links[0]
    assert link.document_id == "doc-004"
    assert link.locator == "chars:50-60"
    assert link.source_kind == "extraction"
    assert link.chunk_id is None
    assert link.source_chunk_index is None
    assert link.page_number is None
    assert link.section_hint is None
    assert link.span_start is None
    assert link.span_end is None
    assert link.excerpt is None


def test_resolve_provenance_links_no_regression_on_existing_evidence_output():
    candidate = {
        "document_id": "doc-005",
        "source_span": {"locator": "chunk-007", "source_kind": "extraction"},
    }

    links = resolve_provenance_links(candidate)

    assert len(links) == 1
    assert links[0].document_id == "doc-005"
    assert links[0].locator == "chunk-007"
    assert links[0].source_kind == "extraction"


def test_resolve_source_availability_all_sources_true():
    bundle = {
        "source_flags": {
            "PatentApi": True,
            "SeedCorpus": True,
            "LlmResearch": True,
        }
    }

    availability = resolve_source_availability(bundle)

    assert availability == {
        "patent_api": True,
        "vector_corpus": True,
        "llm_deep_research": True,
    }


def test_resolve_source_availability_only_seed_corpus_true():
    bundle = {
        "source_flags": {
            "PatentApi": False,
            "SeedCorpus": True,
            "LlmResearch": False,
        }
    }

    availability = resolve_source_availability(bundle)

    assert availability == {
        "patent_api": False,
        "vector_corpus": True,
        "llm_deep_research": False,
    }


def test_resolve_source_availability_none_bundle_returns_all_false():
    availability = resolve_source_availability(None)

    assert availability == {
        "patent_api": False,
        "vector_corpus": False,
        "llm_deep_research": False,
    }


def test_resolve_source_availability_missing_source_flags_returns_all_false():
    bundle = {}

    availability = resolve_source_availability(bundle)

    assert availability == {
        "patent_api": False,
        "vector_corpus": False,
        "llm_deep_research": False,
    }


def test_resolve_source_availability_partial_flags_defaults_missing_to_false():
    bundle = {"source_flags": {"SeedCorpus": True}}

    availability = resolve_source_availability(bundle)

    assert availability == {
        "patent_api": False,
        "vector_corpus": True,
        "llm_deep_research": False,
    }


def test_resolve_source_availability_handles_string_keys():
    bundle = {
        "source_flags": {
            "PatentApi": True,
            "SeedCorpus": False,
            "LlmResearch": True,
        }
    }

    availability = resolve_source_availability(bundle)

    assert "patent_api" in availability
    assert "vector_corpus" in availability
    assert "llm_deep_research" in availability
    assert availability["patent_api"] is True
    assert availability["vector_corpus"] is False
    assert availability["llm_deep_research"] is True


def test_resolve_source_availability_coerces_truthy_values():
    bundle = {
        "source_flags": {
            "PatentApi": 1,
            "SeedCorpus": "yes",
            "LlmResearch": 0,
        }
    }

    availability = resolve_source_availability(bundle)

    assert availability["patent_api"] is True
    assert availability["vector_corpus"] is True
    assert availability["llm_deep_research"] is False


def test_resolve_source_status_all_active():
    bundle = {
        "source_status": {
            "PatentApi": "active",
            "SeedCorpus": "active",
            "LlmResearch": "active",
        }
    }

    status = resolve_source_status(bundle)

    assert status == {
        "patent_api": "active",
        "vector_corpus": "active",
        "llm_deep_research": "active",
    }


def test_resolve_source_status_llm_filtered_flows_through():
    bundle = {
        "source_status": {
            "PatentApi": "active",
            "SeedCorpus": "empty",
            "LlmResearch": "filtered",
        }
    }

    status = resolve_source_status(bundle)

    assert status == {
        "patent_api": "active",
        "vector_corpus": "empty",
        "llm_deep_research": "filtered",
    }


def test_resolve_source_status_none_bundle_returns_all_empty():
    status = resolve_source_status(None)

    assert status == {
        "patent_api": "empty",
        "vector_corpus": "empty",
        "llm_deep_research": "empty",
    }


def test_resolve_source_status_missing_source_status_defaults_to_empty():
    # Backward compatibility: older evidence_completed events / bundles
    # predating this field carry no source_status key at all.
    bundle = {"source_flags": {"PatentApi": True}}

    status = resolve_source_status(bundle)

    assert status == {
        "patent_api": "empty",
        "vector_corpus": "empty",
        "llm_deep_research": "empty",
    }


def test_resolve_source_status_partial_map_defaults_missing_to_empty():
    bundle = {"source_status": {"LlmResearch": "timeout"}}

    status = resolve_source_status(bundle)

    assert status == {
        "patent_api": "empty",
        "vector_corpus": "empty",
        "llm_deep_research": "timeout",
    }


def test_resolve_source_status_error_and_timeout_values_flow_through():
    bundle = {
        "source_status": {
            "PatentApi": "error",
            "SeedCorpus": "timeout",
            "LlmResearch": "active",
        }
    }

    status = resolve_source_status(bundle)

    assert status == {
        "patent_api": "error",
        "vector_corpus": "timeout",
        "llm_deep_research": "active",
    }


def _views_by_type(evidence_bundle: dict | None) -> dict:
    return {view["source_type"]: view for view in resolve_evidence_source_views(evidence_bundle)}


def test_resolve_evidence_source_views_returns_all_three_sources_for_none_bundle():
    views = _views_by_type(None)

    assert set(views) == {"patent_api", "vector_corpus", "llm_deep_research"}
    for view in views.values():
        assert view["available"] is False
        assert view["status"] == "empty"
        assert view["hits"] == []
        assert view["confidence"] == 0.0
        assert view["reasoning"] == []
        assert view["degraded"] is False
        assert view["degraded_sources"] == []


def test_resolve_evidence_source_views_surfaces_hits_even_when_scorer_cited_nothing():
    # DEFECT 1: a source with genuine bundle hits must list them regardless of
    # whether any axis cited them -- this function never looks at axes at all.
    bundle = {
        "source_flags": {"PatentApi": True, "SeedCorpus": False, "LlmResearch": False},
        "source_status": {"PatentApi": "active"},
        "hits": [
            _hit(HIT_LOWEST_PATENT_ID, "hash-a", "Uncited USPTO hit", "PatentApi"),
        ],
    }

    views = _views_by_type(bundle)

    patent_view = views["patent_api"]
    assert patent_view["available"] is True
    assert len(patent_view["hits"]) == 1
    assert patent_view["hits"][0]["patent_id"] == HIT_LOWEST_PATENT_ID
    assert patent_view["hits"][0]["title"] == "Uncited USPTO hit"


def test_resolve_evidence_source_views_groups_multi_source_hit_under_every_source():
    bundle = {
        "hits": [
            {
                "patent_id": HIT_LOWEST_PATENT_ID,
                "content_hash": "hash-multi",
                "title": "Matched by patent API and corpus",
                "url": "https://example.com/patent/multi",
                "similarity": 0.6,
                "sources": ["PatentApi", "SeedCorpus"],
            },
        ],
    }

    views = _views_by_type(bundle)

    assert len(views["patent_api"]["hits"]) == 1
    assert len(views["vector_corpus"]["hits"]) == 1
    assert views["llm_deep_research"]["hits"] == []
    assert views["patent_api"]["hits"][0]["patent_id"] == HIT_LOWEST_PATENT_ID
    assert views["vector_corpus"]["hits"][0]["patent_id"] == HIT_LOWEST_PATENT_ID


def test_resolve_evidence_source_views_confidence_is_mean_similarity_per_source():
    bundle = {
        "hits": [
            {
                "patent_id": HIT_LOWEST_PATENT_ID,
                "content_hash": "hash-1",
                "title": "Hit one",
                "url": "https://example.com/patent/1",
                "similarity": 0.4,
                "sources": ["PatentApi"],
            },
            {
                "patent_id": HIT_MIDDLE_PATENT_ID,
                "content_hash": "hash-2",
                "title": "Hit two",
                "url": "https://example.com/patent/2",
                "similarity": 0.8,
                "sources": ["PatentApi"],
            },
        ],
    }

    views = _views_by_type(bundle)

    assert views["patent_api"]["confidence"] == pytest.approx(0.6)


def test_resolve_evidence_source_views_llm_reasoning_and_confidence_from_llm_research():
    bundle = {
        "source_flags": {"LlmResearch": True},
        "hits": [
            {
                "patent_id": HIT_HIGHEST_PATENT_ID,
                "content_hash": "hash-llm",
                "title": "LLM-cited prior art",
                "url": "https://example.com/patent/llm",
                "similarity": 0.3,
                "sources": ["LlmResearch"],
            },
        ],
        "llm_research": {
            "findings": [
                "Claim 1 is not anticipated by any single cited reference.",
                "The combination step is not suggested by the prior art.",
            ],
            "confidence": 0.82,
        },
    }

    views = _views_by_type(bundle)
    llm_view = views["llm_deep_research"]

    assert llm_view["reasoning"] == [
        "Claim 1 is not anticipated by any single cited reference.",
        "The combination step is not suggested by the prior art.",
    ]
    assert llm_view["confidence"] == 0.82
    assert views["patent_api"]["reasoning"] == []
    assert views["vector_corpus"]["reasoning"] == []


def test_resolve_evidence_source_views_degrades_gracefully_without_llm_research_field():
    bundle = {
        "source_flags": {"LlmResearch": True},
        "hits": [],
    }

    views = _views_by_type(bundle)
    llm_view = views["llm_deep_research"]

    assert llm_view["reasoning"] == []
    assert llm_view["confidence"] == 0.0


def test_resolve_evidence_source_views_degraded_flags_only_on_patent_api_card():
    bundle = {
        "degraded": True,
        "degraded_sources": ["Lens"],
        "hits": [],
    }

    views = _views_by_type(bundle)

    assert views["patent_api"]["degraded"] is True
    assert views["patent_api"]["degraded_sources"] == ["Lens"]
    assert views["vector_corpus"]["degraded"] is False
    assert views["vector_corpus"]["degraded_sources"] == []
    assert views["llm_deep_research"]["degraded"] is False
    assert views["llm_deep_research"]["degraded_sources"] == []


def test_resolve_evidence_source_views_status_and_availability_match_existing_resolvers():
    bundle = {
        "source_flags": {"PatentApi": True, "SeedCorpus": False, "LlmResearch": True},
        "source_status": {"PatentApi": "active", "SeedCorpus": "empty", "LlmResearch": "filtered"},
        "hits": [],
    }

    views = _views_by_type(bundle)

    assert views["patent_api"]["available"] is True
    assert views["patent_api"]["status"] == "active"
    assert views["vector_corpus"]["available"] is False
    assert views["vector_corpus"]["status"] == "empty"
    assert views["llm_deep_research"]["available"] is True
    assert views["llm_deep_research"]["status"] == "filtered"
