from seeding.application.report.seeded_report_assembler import (
    assemble_opportunities,
    build_concept_map,
)
from seeding.domain.models.landscape import (
    ConceptLandscape,
    CorpusMatch,
    LiveMatch,
    PriorArtLandscape,
    WhitespaceIntersection,
)

BATCH_ID = "batch-r"


def _candidate(candidate_id: str, **overrides) -> dict:
    doc = {
        "id": candidate_id,
        "batch_id": BATCH_ID,
        "document_id": "doc-1",
        "engine": "seeding",
        "claim_text": "A method comprising X.",
        "problem": "problem text",
        "mechanism": "mechanism text",
        "title": "Candidate title",
        "description": "Candidate description",
        "category": "Whitespace",
        "novelty_delta": "novel bit",
        "roadmap_alignment": "roadmap",
        "round_index": 1,
        "provenance": {"chunk_ids": ["c1"], "excerpts": []},
    }
    doc.update(overrides)
    return doc


def _verdict(candidate_id: str, composite: float = 62.5) -> dict:
    return {
        "candidate_id": candidate_id,
        "batch_id": BATCH_ID,
        "composite_score": composite,
        "axes": {
            "Novelty": {"score": 70, "refs": ["E1", "E2"]},
            "Patentability": {"score": 55, "refs": ["E1"]},
        },
    }


def _bundle(candidate_id: str) -> dict:
    return {
        "id": f"bundle-{candidate_id}",
        "candidate_id": candidate_id,
        "source_flags": {"PatentApi": True, "SeedCorpus": True, "LlmResearch": False},
        "source_status": {"PatentApi": "active", "SeedCorpus": "active"},
        "hits": [
            {
                "patent_id": "US1",
                "content_hash": "h1",
                "title": "Prior art one",
                "url": "http://p/1",
                "similarity": 0.8,
                "sources": ["PatentApi"],
            },
            {
                "patent_id": "US2",
                "content_hash": "h2",
                "title": "Prior art two",
                "url": "http://p/2",
                "similarity": 0.7,
                "sources": ["SeedCorpus"],
            },
        ],
    }


def test_join_produces_opportunity_with_validation_fields():
    candidates = [_candidate("seed-1")]
    verdicts = [_verdict("seed-1")]
    bundles = {"seed-1": _bundle("seed-1")}

    result = assemble_opportunities(candidates, verdicts, bundles)

    assert len(result) == 1
    opp = result[0]
    assert opp.candidate_id == "seed-1"
    assert opp.weighted_score == 62.5
    assert opp.axes["Novelty"].score == 70
    assert opp.axes["Novelty"].refs == ["E1", "E2"]
    assert opp.evidence_bundle_id == "bundle-seed-1"
    assert opp.source_availability == {
        "patent_api": True,
        "vector_corpus": True,
        "llm_deep_research": False,
    }
    assert opp.claim_statement == "A method comprising X."
    assert opp.mechanism == "mechanism text"


def test_join_resolves_citations_from_bundle():
    result = assemble_opportunities(
        [_candidate("seed-1")], [_verdict("seed-1")], {"seed-1": _bundle("seed-1")}
    )
    refs = {citation.ref for citation in result[0].citations}
    assert refs == {"E1", "E2"}


def test_verdict_less_candidates_are_dropped():
    candidates = [_candidate("seed-1"), _candidate("seed-2")]
    verdicts = [_verdict("seed-1")]

    result = assemble_opportunities(candidates, verdicts, {})

    assert [opp.candidate_id for opp in result] == ["seed-1"]


def test_verdict_without_matching_candidate_is_skipped():
    result = assemble_opportunities([_candidate("seed-1")], [_verdict("orphan")], {})
    assert result == []


def test_missing_bundle_yields_empty_citations_and_false_availability():
    result = assemble_opportunities([_candidate("seed-1")], [_verdict("seed-1")], {})
    opp = result[0]
    assert opp.citations == []
    assert opp.source_availability == {
        "patent_api": False,
        "vector_corpus": False,
        "llm_deep_research": False,
    }
    assert opp.evidence_bundle_id == ""


def _concept(concept: str, chunk_ids: list[str], **overrides) -> ConceptLandscape:
    data = {
        "concept": concept,
        "chunk_ids": chunk_ids,
        "density": "dense",
        "corpus_axis": "dense",
        "live_axis": "sparse",
        "live_matches": [
            LiveMatch(
                reference="US-9",
                title="Prior art nine",
                url="http://p/9",
                source="USPTO",
                relevance_score=0.9,
            ),
            LiveMatch(
                reference="US-3",
                title="Prior art three",
                url="http://p/3",
                source="EPO",
                relevance_score=0.3,
            ),
        ],
        "corpus_matches": [
            CorpusMatch(id="corpus-1", score=0.5, section_label="Method", text_excerpt="excerpt"),
        ],
    }
    data.update(overrides)
    return ConceptLandscape(**data)


def _landscape(concepts: list[ConceptLandscape], **overrides) -> PriorArtLandscape:
    data = {
        "id": "landscape-1",
        "batch_id": BATCH_ID,
        "document_id": "doc-1",
        "schema_version": "1.0",
        "concepts": concepts,
    }
    data.update(overrides)
    return PriorArtLandscape(**data)


def test_prior_art_proximity_joins_on_chunk_ids():
    candidates = [_candidate("seed-1", provenance={"chunk_ids": ["c1"], "excerpts": []})]
    landscape = _landscape([_concept("Memory", ["c1"])])

    result = assemble_opportunities(candidates, [_verdict("seed-1")], {}, landscape)

    proximity = result[0].prior_art_proximity
    assert [match.reference for match in proximity] == ["US-9", "US-3", "corpus-1"]
    live_nine = proximity[0]
    assert live_nine.url == "http://p/9"
    assert live_nine.source == "USPTO"
    corpus = proximity[-1]
    assert corpus.url == ""
    assert corpus.note == "corpus match — no external link"


def test_prior_art_proximity_joins_on_target_concept_when_no_chunk_overlap():
    candidates = [
        _candidate(
            "seed-1",
            provenance={"chunk_ids": ["cX"], "excerpts": []},
            target_concept="Memory",
        )
    ]
    landscape = _landscape([_concept("memory", ["cZ"])])

    result = assemble_opportunities(candidates, [_verdict("seed-1")], {}, landscape)

    assert result[0].target_concept == "Memory"
    assert [match.reference for match in result[0].prior_art_proximity] == [
        "US-9",
        "US-3",
        "corpus-1",
    ]


def test_empty_landscape_yields_no_proximity_and_never_fabricates():
    candidates = [_candidate("seed-1", provenance={"chunk_ids": ["c1"], "excerpts": []})]

    result = assemble_opportunities(candidates, [_verdict("seed-1")], {}, None)

    assert result[0].prior_art_proximity == []
    assert build_concept_map(result, None) == []


def test_no_matching_concept_yields_no_proximity():
    candidates = [_candidate("seed-1", provenance={"chunk_ids": ["c9"], "excerpts": []})]
    landscape = _landscape([_concept("Memory", ["c1"])])

    result = assemble_opportunities(candidates, [_verdict("seed-1")], {}, landscape)

    assert result[0].prior_art_proximity == []


def test_concept_map_groups_opportunity_ids_by_matched_concept():
    candidates = [
        _candidate("seed-1", provenance={"chunk_ids": ["c1"], "excerpts": []}),
        _candidate("seed-2", provenance={"chunk_ids": ["c1", "c2"], "excerpts": []}),
    ]
    verdicts = [_verdict("seed-1"), _verdict("seed-2")]
    landscape = _landscape(
        [_concept("Memory", ["c1"]), _concept("Throughput", ["c9"])],
        whitespace=[WhitespaceIntersection(kind="gap", chunk_ids=["c1"])],
    )

    result = assemble_opportunities(candidates, verdicts, {}, landscape)
    concept_map = build_concept_map(result, landscape)

    assert len(concept_map) == 1
    entry = concept_map[0]
    assert entry.concept == "Memory"
    assert set(entry.opportunity_ids) == {"seed-1", "seed-2"}
    assert entry.density == "dense"
    assert entry.corpus_axis == "dense"
    assert entry.live_axis == "sparse"
    assert entry.whitespace is True


def test_concept_map_omits_concepts_with_no_opportunities():
    candidates = [_candidate("seed-1", provenance={"chunk_ids": ["c1"], "excerpts": []})]
    landscape = _landscape([_concept("Throughput", ["c9"])])

    result = assemble_opportunities(candidates, [_verdict("seed-1")], {}, landscape)

    assert build_concept_map(result, landscape) == []
