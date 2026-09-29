import pytest

from seeding.domain.services.evidence_citation_resolver import resolve_evidence_source_views


def _bundle(**overrides) -> dict:
    base = {
        "source_flags": {"PatentApi": True, "SeedCorpus": True, "LlmResearch": True},
        "source_status": {"PatentApi": "active", "SeedCorpus": "active", "LlmResearch": "active"},
        "hits": [],
    }
    base.update(overrides)
    return base


def _views_by_type(bundle: dict) -> dict[str, dict]:
    return {view["source_type"]: view for view in resolve_evidence_source_views(bundle)}


def test_available_but_uncited_source_still_lists_its_own_hits():
    bundle = _bundle(
        hits=[
            {
                "patent_id": "US1",
                "content_hash": "h1",
                "title": "Patent one",
                "url": "http://p/1",
                "similarity": 0.9,
                "sources": ["PatentApi"],
                "jurisdiction": "US",
            }
        ],
    )

    views = _views_by_type(bundle)
    patent_view = views["patent_api"]

    assert patent_view["available"] is True
    assert patent_view["status"] == "active"
    assert len(patent_view["hits"]) == 1
    assert patent_view["hits"][0]["title"] == "Patent one"
    assert patent_view["hits"][0]["patent_id"] == "US1"
    assert patent_view["hits"][0]["jurisdiction"] == "US"


def test_multi_source_hit_appears_on_every_owning_card():
    bundle = _bundle(
        hits=[
            {
                "patent_id": "US2",
                "content_hash": "h2",
                "title": "Shared hit",
                "url": "http://p/2",
                "similarity": 0.6,
                "sources": ["PatentApi", "SeedCorpus"],
            }
        ],
    )

    views = _views_by_type(bundle)

    assert len(views["patent_api"]["hits"]) == 1
    assert len(views["vector_corpus"]["hits"]) == 1
    assert views["patent_api"]["hits"][0]["title"] == "Shared hit"
    assert views["vector_corpus"]["hits"][0]["title"] == "Shared hit"
    assert views["llm_deep_research"]["hits"] == []


def test_confidence_is_mean_similarity_of_that_sources_own_hits():
    bundle = _bundle(
        hits=[
            {
                "patent_id": "US3",
                "content_hash": "h3",
                "title": "Hit A",
                "url": "http://p/3",
                "similarity": 0.4,
                "sources": ["PatentApi"],
            },
            {
                "patent_id": "US4",
                "content_hash": "h4",
                "title": "Hit B",
                "url": "http://p/4",
                "similarity": 0.8,
                "sources": ["PatentApi"],
            },
        ],
    )

    views = _views_by_type(bundle)

    assert views["patent_api"]["confidence"] == pytest.approx(0.6)


def test_llm_reasoning_and_confidence_attached_from_llm_research():
    bundle = _bundle(
        hits=[
            {
                "patent_id": None,
                "content_hash": "h5",
                "title": "citation-1",
                "url": "",
                "similarity": 0.5,
                "sources": ["LlmResearch"],
            }
        ],
        llm_research={"findings": ["Prior art does not disclose X."], "confidence": 0.72},
    )

    views = _views_by_type(bundle)
    llm_view = views["llm_deep_research"]

    assert llm_view["reasoning"] == ["Prior art does not disclose X."]
    assert llm_view["confidence"] == 0.72
    assert len(llm_view["hits"]) == 1


def test_missing_llm_research_yields_empty_reasoning_and_no_error():
    bundle = _bundle(hits=[])

    views = _views_by_type(bundle)
    llm_view = views["llm_deep_research"]

    assert llm_view["reasoning"] == []
    assert llm_view["confidence"] == 0.0


def test_degraded_flags_only_attached_to_patent_api_card():
    bundle = _bundle(hits=[], degraded=True, degraded_sources=["Lens"])

    views = _views_by_type(bundle)

    assert views["patent_api"]["degraded"] is True
    assert views["patent_api"]["degraded_sources"] == ["Lens"]
    assert "degraded" not in views["vector_corpus"]
    assert "degraded" not in views["llm_deep_research"]


def test_none_bundle_yields_unavailable_empty_views_for_all_sources():
    views = _views_by_type(None)

    assert set(views.keys()) == {"patent_api", "vector_corpus", "llm_deep_research"}
    for view in views.values():
        assert view["available"] is False
        assert view["hits"] == []
        assert view["confidence"] == 0.0
        assert view["reasoning"] == []
