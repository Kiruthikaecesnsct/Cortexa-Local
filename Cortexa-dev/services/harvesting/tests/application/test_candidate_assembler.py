from harvesting.application.candidate_assembler import ChunkGeometryRepos, assemble_candidates
from harvesting.domain.models.rank_weights import RankWeights
from harvesting.infrastructure.config.settings import HarvestingSettings

DEFAULT_WEIGHTS = RankWeights(novelty=0.4, feasibility=0.25, strategic=0.2, patentability=0.15)
DEFAULT_SETTINGS = HarvestingSettings(
    cosmos_uri="https://test.example.com",
    servicebus_namespace_fqdn="test.servicebus.windows.net",
)


class _StubChunkRepo:
    def __init__(self, chunk: dict | None = None, error: Exception | None = None) -> None:
        self._chunk = chunk
        self._error = error

    async def get_by_document_order(self, batch_id, document_id, order_index):
        if self._error is not None:
            raise self._error
        return self._chunk


class _StubDocumentRepo:
    def __init__(self, document: dict | None = None) -> None:
        self._document = document

    async def get(self, batch_id, document_id):
        return self._document


class _StubProvenanceRepo:
    def __init__(self, entry: dict | None = None) -> None:
        self._entry = entry

    async def get(self, batch_id, chunk_id):
        return self._entry


def _pdf_candidate() -> dict:
    return {
        "id": "cand-pdf",
        "batch_id": "batch-001",
        "document_id": "doc-001",
        "source_chunk_index": 2,
        "claim_text": "A method for doing X.",
        "title": "Invention X",
        "description": "This invention solves X.",
        "source_span": {
            "locator": "chars:0-30",
            "source_kind": "paper",
            "span_start": 0,
            "span_end": 30,
            "excerpt": "A method for doing X here.",
        },
    }


def _all_verdict_axes() -> dict[str, dict]:
    return {
        "Novelty": {"score": 80, "refs": ["ref-1"]},
        "Inventiveness": {"score": 70, "refs": ["ref-2"]},
        "Commercial": {"score": 60, "refs": []},
        "Strategic": {"score": 50, "refs": []},
        "Patentability": {"score": 75, "refs": ["ref-3"]},
    }


async def test_assemble_candidates_with_real_cosmos_schema_produces_non_empty_result():
    candidates = [
        {
            "id": "cand-1",
            "batch_id": "batch-001",
            "document_id": "doc-001",
            "claim_text": "A method for doing X.",
            "title": "Invention X",
            "description": "This invention solves X.",
            "citations": ["https://example.com/patent/123"],
            "provenance_links": ["chunk-001"],
        },
        {
            "id": "cand-2",
            "batch_id": "batch-001",
            "document_id": "doc-001",
            "claim_text": "A system for doing Y.",
            "title": "Invention Y",
            "description": "This invention solves Y.",
            "citations": [],
            "provenance_links": ["chunk-002"],
        },
    ]
    verdicts = [
        {
            "candidate_id": "cand-1",
            "batch_id": "batch-001",
            "document_id": "doc-001",
            "axes": _all_verdict_axes(),
        },
        {
            "candidate_id": "cand-2",
            "batch_id": "batch-001",
            "document_id": "doc-001",
            "axes": _all_verdict_axes(),
        },
    ]
    result = await assemble_candidates(candidates, verdicts, DEFAULT_WEIGHTS, DEFAULT_SETTINGS)

    assert len(result) == 2
    assert result[0].candidate_id in {"cand-1", "cand-2"}
    assert result[1].candidate_id in {"cand-1", "cand-2"}
    assert result[0].candidate_id != result[1].candidate_id
    assert all(len(c.axes) == 5 for c in result)
    assert all(c.rank in {1, 2} for c in result)


async def test_seeded_verdicts_never_enter_report_when_candidate_excluded():
    candidates = [
        {
            "id": "cand-harvest",
            "batch_id": "batch-001",
            "document_id": "doc-001",
            "claim_text": "A method for doing X.",
            "title": "Invention X",
            "description": "This invention solves X.",
        }
    ]
    verdicts = [
        {
            "candidate_id": "cand-harvest",
            "batch_id": "batch-001",
            "document_id": "doc-001",
            "axes": _all_verdict_axes(),
        },
        {
            "candidate_id": "cand-seeding",
            "batch_id": "batch-001",
            "document_id": "doc-001",
            "axes": _all_verdict_axes(),
        },
    ]

    result = await assemble_candidates(candidates, verdicts, DEFAULT_WEIGHTS, DEFAULT_SETTINGS)

    result_ids = {c.candidate_id for c in result}
    assert result_ids == {"cand-harvest"}
    assert "cand-seeding" not in result_ids


async def test_assemble_candidates_joins_on_candidate_id_field_in_verdicts():
    candidates = [
        {
            "id": "cand-alpha",
            "batch_id": "batch-001",
            "document_id": "doc-001",
            "claim_text": "Claim text.",
            "title": "Title Alpha",
            "description": "Description.",
        }
    ]
    verdicts = [
        {
            "candidate_id": "cand-alpha",
            "batch_id": "batch-001",
            "document_id": "doc-001",
            "axes": _all_verdict_axes(),
        }
    ]
    result = await assemble_candidates(candidates, verdicts, DEFAULT_WEIGHTS, DEFAULT_SETTINGS)

    assert len(result) == 1
    assert result[0].candidate_id == "cand-alpha"


async def test_assemble_candidates_skips_verdicts_with_no_matching_candidate():
    candidates = [
        {
            "id": "cand-1",
            "batch_id": "batch-001",
            "document_id": "doc-001",
            "claim_text": "Claim.",
            "title": "Title 1",
            "description": "Desc 1.",
        }
    ]
    verdicts = [
        {
            "candidate_id": "cand-1",
            "batch_id": "batch-001",
            "document_id": "doc-001",
            "axes": _all_verdict_axes(),
        },
        {
            "candidate_id": "cand-999",
            "batch_id": "batch-001",
            "document_id": "doc-001",
            "axes": _all_verdict_axes(),
        },
    ]
    result = await assemble_candidates(candidates, verdicts, DEFAULT_WEIGHTS, DEFAULT_SETTINGS)

    assert len(result) == 1
    assert result[0].candidate_id == "cand-1"


async def test_assemble_candidates_returns_empty_when_no_verdicts_match():
    candidates = [
        {
            "id": "cand-1",
            "batch_id": "batch-001",
            "document_id": "doc-001",
            "claim_text": "Claim.",
            "title": "Title 1",
            "description": "Desc 1.",
        }
    ]
    verdicts = [
        {
            "candidate_id": "cand-999",
            "batch_id": "batch-001",
            "document_id": "doc-001",
            "axes": _all_verdict_axes(),
        }
    ]
    result = await assemble_candidates(candidates, verdicts, DEFAULT_WEIGHTS, DEFAULT_SETTINGS)

    assert len(result) == 0


async def test_assemble_candidates_handles_candidate_id_fallback_field():
    candidates = [
        {
            "candidate_id": "cand-legacy",
            "batch_id": "batch-001",
            "document_id": "doc-001",
            "claim_text": "Claim.",
            "title": "Legacy",
            "description": "Desc.",
        }
    ]
    verdicts = [
        {
            "candidate_id": "cand-legacy",
            "batch_id": "batch-001",
            "document_id": "doc-001",
            "axes": _all_verdict_axes(),
        }
    ]
    result = await assemble_candidates(candidates, verdicts, DEFAULT_WEIGHTS, DEFAULT_SETTINGS)

    assert len(result) == 1
    assert result[0].candidate_id == "cand-legacy"


async def test_assemble_candidates_sorts_by_rank():
    candidates = [
        {
            "id": "cand-1",
            "batch_id": "batch-001",
            "document_id": "doc-001",
            "claim_text": "Low score.",
            "title": "Low",
            "description": "Low scorer.",
        },
        {
            "id": "cand-2",
            "batch_id": "batch-001",
            "document_id": "doc-001",
            "claim_text": "High score.",
            "title": "High",
            "description": "High scorer.",
        },
    ]
    verdicts = [
        {
            "candidate_id": "cand-1",
            "batch_id": "batch-001",
            "document_id": "doc-001",
            "axes": {
                "Novelty": {"score": 30, "refs": []},
                "Inventiveness": {"score": 30, "refs": []},
                "Commercial": {"score": 30, "refs": []},
                "Strategic": {"score": 30, "refs": []},
                "Patentability": {"score": 30, "refs": []},
            },
        },
        {
            "candidate_id": "cand-2",
            "batch_id": "batch-001",
            "document_id": "doc-001",
            "axes": {
                "Novelty": {"score": 90, "refs": []},
                "Inventiveness": {"score": 90, "refs": []},
                "Commercial": {"score": 90, "refs": []},
                "Strategic": {"score": 90, "refs": []},
                "Patentability": {"score": 90, "refs": []},
            },
        },
    ]
    result = await assemble_candidates(candidates, verdicts, DEFAULT_WEIGHTS, DEFAULT_SETTINGS)

    assert len(result) == 2
    assert result[0].rank == 1
    assert result[1].rank == 2
    assert result[0].candidate_id == "cand-2"
    assert result[1].candidate_id == "cand-1"


async def test_derive_title_from_extraction_candidate_with_no_title_field():
    claim_text = (
        "A momentum-corrected error-feedback buffer accumulates "
        "dropped gradient components so no signal is lost. Extra sentence."
    )
    candidates = [
        {
            "candidate_id": "c1",
            "claim_text": claim_text,
            "problem": "gradient loss under sparsification",
            "mechanism": "error-feedback accumulation",
            "tech_field": "machine learning",
            "batch_id": "b1",
            "document_id": "doc-1",
        }
    ]
    verdicts = [
        {
            "candidate_id": "c1",
            "batch_id": "b1",
            "document_id": "doc-1",
            "axes": _all_verdict_axes(),
        }
    ]
    result = await assemble_candidates(candidates, verdicts, DEFAULT_WEIGHTS, DEFAULT_SETTINGS)

    assert len(result) == 1
    assert result[0].title != ""
    assert "momentum-corrected error-feedback buffer" in result[0].title
    assert "Extra sentence" not in result[0].title
    assert result[0].description != ""
    assert "gradient loss under sparsification" in result[0].description
    assert "error-feedback accumulation" in result[0].description


async def test_derive_title_falls_back_to_problem_when_claim_text_empty():
    candidates = [
        {
            "candidate_id": "c2",
            "claim_text": "",
            "problem": "slow convergence in distributed training",
            "mechanism": "asynchronous parameter updates",
            "tech_field": "deep learning",
            "batch_id": "b1",
            "document_id": "doc-1",
        }
    ]
    verdicts = [
        {
            "candidate_id": "c2",
            "batch_id": "b1",
            "document_id": "doc-1",
            "axes": _all_verdict_axes(),
        }
    ]
    result = await assemble_candidates(candidates, verdicts, DEFAULT_WEIGHTS, DEFAULT_SETTINGS)

    assert len(result) == 1
    assert result[0].title == "slow convergence in distributed training"


async def test_derive_title_falls_back_to_tech_field_when_only_tech_field_present():
    candidates = [
        {
            "candidate_id": "c3",
            "claim_text": "",
            "problem": "",
            "tech_field": "neural network optimization",
            "batch_id": "b1",
            "document_id": "doc-1",
        }
    ]
    verdicts = [
        {
            "candidate_id": "c3",
            "batch_id": "b1",
            "document_id": "doc-1",
            "axes": _all_verdict_axes(),
        }
    ]
    result = await assemble_candidates(candidates, verdicts, DEFAULT_WEIGHTS, DEFAULT_SETTINGS)

    assert len(result) == 1
    assert result[0].title == "neural network optimization"


async def test_derive_title_caps_long_claim_text_with_ellipsis():
    long_claim = "A" * 100 + "."
    candidates = [
        {
            "candidate_id": "c4",
            "claim_text": long_claim,
            "batch_id": "b1",
            "document_id": "doc-1",
        }
    ]
    verdicts = [
        {
            "candidate_id": "c4",
            "batch_id": "b1",
            "document_id": "doc-1",
            "axes": _all_verdict_axes(),
        }
    ]
    result = await assemble_candidates(candidates, verdicts, DEFAULT_WEIGHTS, DEFAULT_SETTINGS)

    assert len(result) == 1
    assert len(result[0].title) <= 81
    assert result[0].title.endswith("…")


async def test_assemble_candidates_resolves_citations_from_matching_evidence_bundle():
    candidates = [
        {
            "id": "cand-1",
            "batch_id": "batch-001",
            "document_id": "doc-001",
            "claim_text": "A method for doing X.",
            "title": "Invention X",
            "description": "This invention solves X.",
        }
    ]
    verdicts = [
        {
            "candidate_id": "cand-1",
            "batch_id": "batch-001",
            "document_id": "doc-001",
            "axes": {
                "Novelty": {"score": 80, "refs": ["E1", "E2"]},
                "Inventiveness": {"score": 70, "refs": []},
                "Commercial": {"score": 60, "refs": []},
                "Strategic": {"score": 50, "refs": []},
                "Patentability": {"score": 75, "refs": ["E1"]},
            },
        }
    ]
    evidence_bundles = {
        "cand-1": {
            "hits": [
                {
                    "patent_id": "US2000000A",
                    "content_hash": "hash-2",
                    "title": "Second prior art",
                    "url": "https://example.com/patent/US2000000A",
                    "similarity": 0.7,
                    "sources": ["SeedCorpus"],
                },
                {
                    "patent_id": "US1000000A",
                    "content_hash": "hash-1",
                    "title": "First prior art",
                    "url": "https://example.com/patent/US1000000A",
                    "similarity": 0.9,
                    "sources": ["PatentApi"],
                },
            ]
        }
    }

    result = await assemble_candidates(
        candidates, verdicts, DEFAULT_WEIGHTS, DEFAULT_SETTINGS, evidence_bundles
    )

    assert len(result) == 1
    citations = result[0].citations
    assert citations != []
    assert {c.ref for c in citations} == {"E1", "E2"}
    citation_by_ref = {c.ref: c for c in citations}
    assert citation_by_ref["E1"].title == "First prior art"
    assert citation_by_ref["E1"].patent_id == "US1000000A"
    assert citation_by_ref["E1"].url == "https://example.com/patent/US1000000A"
    assert citation_by_ref["E2"].title == "Second prior art"
    assert citation_by_ref["E2"].patent_id == "US2000000A"


async def test_assemble_candidates_without_matching_evidence_bundle_produces_empty_citations():
    candidates = [
        {
            "id": "cand-1",
            "batch_id": "batch-001",
            "document_id": "doc-001",
            "claim_text": "A method for doing X.",
            "title": "Invention X",
            "description": "This invention solves X.",
        }
    ]
    verdicts = [
        {
            "candidate_id": "cand-1",
            "batch_id": "batch-001",
            "document_id": "doc-001",
            "axes": _all_verdict_axes(),
        }
    ]

    result = await assemble_candidates(
        candidates, verdicts, DEFAULT_WEIGHTS, DEFAULT_SETTINGS, None
    )

    assert len(result) == 1
    assert result[0].citations == []


async def test_assemble_candidates_grounded_axis_refs_never_yield_empty_citations_report():
    # CI guard for BUG134: a candidate whose axes cite evidence refs, backed by
    # a matching evidence bundle, must never surface with an empty citations
    # list in the assembled report -- that was the symptom of the bug.
    candidates = [
        {
            "id": "cand-1",
            "batch_id": "batch-001",
            "document_id": "doc-001",
            "claim_text": "A method for doing X.",
            "title": "Invention X",
            "description": "This invention solves X.",
        },
        {
            "id": "cand-2",
            "batch_id": "batch-001",
            "document_id": "doc-002",
            "claim_text": "A system for doing Y.",
            "title": "Invention Y",
            "description": "This invention solves Y.",
        },
    ]
    verdicts = [
        {
            "candidate_id": "cand-1",
            "batch_id": "batch-001",
            "document_id": "doc-001",
            "axes": {
                "Novelty": {"score": 80, "refs": ["E1"]},
                "Inventiveness": {"score": 70, "refs": []},
                "Commercial": {"score": 60, "refs": []},
                "Strategic": {"score": 50, "refs": []},
                "Patentability": {"score": 75, "refs": []},
            },
        },
        {
            "candidate_id": "cand-2",
            "batch_id": "batch-001",
            "document_id": "doc-002",
            "axes": {
                "Novelty": {"score": 65, "refs": ["E1"]},
                "Inventiveness": {"score": 55, "refs": []},
                "Commercial": {"score": 45, "refs": []},
                "Strategic": {"score": 40, "refs": []},
                "Patentability": {"score": 60, "refs": []},
            },
        },
    ]
    evidence_bundles = {
        candidate["id"]: {
            "hits": [
                {
                    "patent_id": "US1000000A",
                    "content_hash": "hash-1",
                    "title": "First prior art",
                    "url": "https://example.com/patent/US1000000A",
                    "similarity": 0.9,
                    "sources": ["PatentApi"],
                }
            ]
        }
        for candidate in candidates
    }

    result = await assemble_candidates(
        candidates, verdicts, DEFAULT_WEIGHTS, DEFAULT_SETTINGS, evidence_bundles
    )

    grounded_candidates = [c for c in result if any(axis.refs for axis in c.axes.values())]
    assert grounded_candidates, "test fixture must exercise at least one grounded candidate"
    assert all(c.citations for c in grounded_candidates), (
        "grounded candidates with a matching evidence bundle must never report empty citations"
    )


async def test_derive_description_composes_problem_and_mechanism():
    candidates = [
        {
            "candidate_id": "c5",
            "problem": "memory overhead in large models",
            "mechanism": "gradient checkpointing reduces peak memory",
            "claim_text": "Some claim text.",
            "batch_id": "b1",
            "document_id": "doc-1",
        }
    ]
    verdicts = [
        {
            "candidate_id": "c5",
            "batch_id": "b1",
            "document_id": "doc-1",
            "axes": _all_verdict_axes(),
        }
    ]
    result = await assemble_candidates(candidates, verdicts, DEFAULT_WEIGHTS, DEFAULT_SETTINGS)

    assert len(result) == 1
    assert "memory overhead in large models" in result[0].description
    assert "gradient checkpointing reduces peak memory" in result[0].description


async def test_claim_text_propagated_from_candidate_to_dto():
    claim = "A method for accelerating training using gradient accumulation."
    candidates = [
        {
            "candidate_id": "c-claim",
            "claim_text": claim,
            "problem": "slow training",
            "mechanism": "gradient accumulation",
            "batch_id": "b1",
            "document_id": "doc-1",
        }
    ]
    verdicts = [
        {
            "candidate_id": "c-claim",
            "batch_id": "b1",
            "document_id": "doc-1",
            "axes": _all_verdict_axes(),
        }
    ]
    result = await assemble_candidates(candidates, verdicts, DEFAULT_WEIGHTS, DEFAULT_SETTINGS)

    assert len(result) == 1
    assert result[0].claim_text == claim


async def test_claim_text_empty_when_candidate_lacks_claim_text():
    candidates = [
        {
            "candidate_id": "c-no-claim",
            "problem": "memory bottleneck",
            "mechanism": "lazy evaluation",
            "batch_id": "b1",
            "document_id": "doc-1",
        }
    ]
    verdicts = [
        {
            "candidate_id": "c-no-claim",
            "batch_id": "b1",
            "document_id": "doc-1",
            "axes": _all_verdict_axes(),
        }
    ]
    result = await assemble_candidates(candidates, verdicts, DEFAULT_WEIGHTS, DEFAULT_SETTINGS)

    assert len(result) == 1
    assert result[0].claim_text == ""


async def test_claim_text_strips_whitespace():
    candidates = [
        {
            "candidate_id": "c-ws",
            "claim_text": "  A novel method.  ",
            "batch_id": "b1",
            "document_id": "doc-1",
        }
    ]
    verdicts = [
        {
            "candidate_id": "c-ws",
            "batch_id": "b1",
            "document_id": "doc-1",
            "axes": _all_verdict_axes(),
        }
    ]
    result = await assemble_candidates(candidates, verdicts, DEFAULT_WEIGHTS, DEFAULT_SETTINGS)

    assert len(result) == 1
    assert result[0].claim_text == "A novel method."


async def test_source_availability_derived_from_evidence_bundle():
    candidates = [
        {
            "id": "cand-1",
            "batch_id": "batch-001",
            "document_id": "doc-001",
            "claim_text": "A method for doing X.",
            "title": "Invention X",
            "description": "This invention solves X.",
        }
    ]
    verdicts = [
        {
            "candidate_id": "cand-1",
            "batch_id": "batch-001",
            "document_id": "doc-001",
            "axes": _all_verdict_axes(),
        }
    ]
    evidence_bundles = {
        "cand-1": {
            "source_flags": {
                "PatentApi": True,
                "SeedCorpus": False,
                "LlmResearch": True,
            },
            "source_status": {
                "PatentApi": "active",
                "SeedCorpus": "empty",
                "LlmResearch": "active",
            },
            "hits": [],
        }
    }

    result = await assemble_candidates(
        candidates, verdicts, DEFAULT_WEIGHTS, DEFAULT_SETTINGS, evidence_bundles
    )

    assert len(result) == 1
    assert result[0].source_availability == {
        "patent_api": True,
        "vector_corpus": False,
        "llm_deep_research": True,
    }
    assert result[0].source_status == {
        "patent_api": "active",
        "vector_corpus": "empty",
        "llm_deep_research": "active",
    }


async def test_source_availability_all_false_when_no_evidence_bundle():
    candidates = [
        {
            "id": "cand-1",
            "batch_id": "batch-001",
            "document_id": "doc-001",
            "claim_text": "A method for doing X.",
            "title": "Invention X",
            "description": "This invention solves X.",
        }
    ]
    verdicts = [
        {
            "candidate_id": "cand-1",
            "batch_id": "batch-001",
            "document_id": "doc-001",
            "axes": _all_verdict_axes(),
        }
    ]

    result = await assemble_candidates(
        candidates, verdicts, DEFAULT_WEIGHTS, DEFAULT_SETTINGS, None
    )

    assert len(result) == 1
    assert result[0].source_availability == {
        "patent_api": False,
        "vector_corpus": False,
        "llm_deep_research": False,
    }
    assert result[0].source_status == {
        "patent_api": "empty",
        "vector_corpus": "empty",
        "llm_deep_research": "empty",
    }


async def test_source_status_filtered_llm_flows_through_to_assembled_candidate():
    candidates = [
        {
            "id": "cand-1",
            "batch_id": "batch-001",
            "document_id": "doc-001",
            "claim_text": "A method for doing X.",
            "title": "Invention X",
            "description": "This invention solves X.",
        }
    ]
    verdicts = [
        {
            "candidate_id": "cand-1",
            "batch_id": "batch-001",
            "document_id": "doc-001",
            "axes": _all_verdict_axes(),
        }
    ]
    evidence_bundles = {
        "cand-1": {
            "source_flags": {
                "PatentApi": True,
                "SeedCorpus": True,
                "LlmResearch": False,
            },
            "source_status": {
                "PatentApi": "active",
                "SeedCorpus": "active",
                "LlmResearch": "filtered",
            },
            "hits": [],
        }
    }

    result = await assemble_candidates(
        candidates, verdicts, DEFAULT_WEIGHTS, DEFAULT_SETTINGS, evidence_bundles
    )

    assert len(result) == 1
    assert result[0].source_status["llm_deep_research"] == "filtered"


async def test_source_status_defaults_to_empty_when_evidence_bundle_predates_field():
    # Backward compatibility: evidence bundles produced before source_status
    # was introduced carry source_flags but no source_status key at all.
    candidates = [
        {
            "id": "cand-1",
            "batch_id": "batch-001",
            "document_id": "doc-001",
            "claim_text": "A method for doing X.",
            "title": "Invention X",
            "description": "This invention solves X.",
        }
    ]
    verdicts = [
        {
            "candidate_id": "cand-1",
            "batch_id": "batch-001",
            "document_id": "doc-001",
            "axes": _all_verdict_axes(),
        }
    ]
    evidence_bundles = {
        "cand-1": {
            "source_flags": {
                "PatentApi": True,
                "SeedCorpus": False,
                "LlmResearch": True,
            },
            "hits": [],
        }
    }

    result = await assemble_candidates(
        candidates, verdicts, DEFAULT_WEIGHTS, DEFAULT_SETTINGS, evidence_bundles
    )

    assert len(result) == 1
    assert result[0].source_status == {
        "patent_api": "empty",
        "vector_corpus": "empty",
        "llm_deep_research": "empty",
    }


async def test_claim_draft_populated_when_verdict_has_drafted_claim():
    drafted_claim = "A method comprising: a step for X; a step for Y; wherein Z is achieved."
    candidates = [
        {
            "id": "cand-1",
            "batch_id": "batch-001",
            "document_id": "doc-001",
            "claim_text": "A method for doing X.",
            "title": "Invention X",
            "description": "Description.",
        }
    ]
    verdicts = [
        {
            "candidate_id": "cand-1",
            "batch_id": "batch-001",
            "document_id": "doc-001",
            "axes": _all_verdict_axes(),
            "drafted_claim": drafted_claim,
        }
    ]

    result = await assemble_candidates(candidates, verdicts, DEFAULT_WEIGHTS, DEFAULT_SETTINGS)

    assert len(result) == 1
    assert result[0].claim_draft == drafted_claim


async def test_claim_draft_empty_when_verdict_has_empty_drafted_claim():
    candidates = [
        {
            "id": "cand-1",
            "batch_id": "batch-001",
            "document_id": "doc-001",
            "claim_text": "A method for doing X.",
            "title": "Invention X",
            "description": "Description.",
        }
    ]
    verdicts = [
        {
            "candidate_id": "cand-1",
            "batch_id": "batch-001",
            "document_id": "doc-001",
            "axes": _all_verdict_axes(),
            "drafted_claim": "",
        }
    ]

    result = await assemble_candidates(candidates, verdicts, DEFAULT_WEIGHTS, DEFAULT_SETTINGS)

    assert len(result) == 1
    assert result[0].claim_draft == ""


async def test_claim_draft_empty_when_verdict_missing_drafted_claim():
    candidates = [
        {
            "id": "cand-1",
            "batch_id": "batch-001",
            "document_id": "doc-001",
            "claim_text": "A method for doing X.",
            "title": "Invention X",
            "description": "Description.",
        }
    ]
    verdicts = [
        {
            "candidate_id": "cand-1",
            "batch_id": "batch-001",
            "document_id": "doc-001",
            "axes": _all_verdict_axes(),
        }
    ]

    result = await assemble_candidates(candidates, verdicts, DEFAULT_WEIGHTS, DEFAULT_SETTINGS)

    assert len(result) == 1
    assert result[0].claim_draft == ""


async def test_evidence_sources_surface_hits_uncited_by_scorer():
    # BUG183 Defect 1: patent-API hits that exist in the bundle but were not
    # cited by any axis must still show up on the assembled candidate's
    # evidence_sources, independent of the (empty) citations list.
    candidates = [
        {
            "id": "cand-1",
            "batch_id": "batch-001",
            "document_id": "doc-001",
            "claim_text": "A method for doing X.",
            "title": "Invention X",
            "description": "This invention solves X.",
        }
    ]
    verdicts = [
        {
            "candidate_id": "cand-1",
            "batch_id": "batch-001",
            "document_id": "doc-001",
            "axes": _all_verdict_axes(),
        }
    ]
    evidence_bundles = {
        "cand-1": {
            "source_flags": {"PatentApi": True, "SeedCorpus": False, "LlmResearch": False},
            "hits": [
                {
                    "patent_id": "US9000000A",
                    "content_hash": "hash-uncited",
                    "title": "Uncited patent hit",
                    "url": "https://example.com/patent/US9000000A",
                    "similarity": 0.65,
                    "sources": ["PatentApi"],
                }
            ],
        }
    }

    result = await assemble_candidates(
        candidates, verdicts, DEFAULT_WEIGHTS, DEFAULT_SETTINGS, evidence_bundles
    )

    assert len(result) == 1
    assert result[0].citations == []
    evidence_sources = {v["source_type"]: v for v in result[0].evidence_sources}
    assert evidence_sources["patent_api"]["available"] is True
    assert len(evidence_sources["patent_api"]["hits"]) == 1
    assert evidence_sources["patent_api"]["hits"][0]["patent_id"] == "US9000000A"


async def test_evidence_sources_empty_when_no_evidence_bundle():
    candidates = [
        {
            "id": "cand-1",
            "batch_id": "batch-001",
            "document_id": "doc-001",
            "claim_text": "A method for doing X.",
            "title": "Invention X",
            "description": "This invention solves X.",
        }
    ]
    verdicts = [
        {
            "candidate_id": "cand-1",
            "batch_id": "batch-001",
            "document_id": "doc-001",
            "axes": _all_verdict_axes(),
        }
    ]

    result = await assemble_candidates(
        candidates, verdicts, DEFAULT_WEIGHTS, DEFAULT_SETTINGS, None
    )

    assert len(result) == 1
    assert len(result[0].evidence_sources) == 3
    assert all(v["hits"] == [] for v in result[0].evidence_sources)


async def test_claim_draft_strips_whitespace():
    candidates = [
        {
            "id": "cand-1",
            "batch_id": "batch-001",
            "document_id": "doc-001",
            "claim_text": "A method for doing X.",
            "title": "Invention X",
            "description": "Description.",
        }
    ]
    verdicts = [
        {
            "candidate_id": "cand-1",
            "batch_id": "batch-001",
            "document_id": "doc-001",
            "axes": _all_verdict_axes(),
            "drafted_claim": "  A method comprising: steps.  ",
        }
    ]

    result = await assemble_candidates(candidates, verdicts, DEFAULT_WEIGHTS, DEFAULT_SETTINGS)

    assert len(result) == 1
    assert result[0].claim_draft == "A method comprising: steps."


async def test_assemble_candidates_with_pdf_geometry_produces_pdf_preview_kind():
    candidates = [_pdf_candidate()]
    verdicts = [
        {
            "candidate_id": "cand-pdf",
            "batch_id": "batch-001",
            "document_id": "doc-001",
            "axes": _all_verdict_axes(),
        }
    ]
    chunk = {
        "id": "doc-001|2",
        "text": "A method for doing X here.",
        "start_char": 0,
        "end_char": 30,
        "chunk_rects": [{"page_number": 1, "x0": 1.0, "x1": 2.0, "top": 3.0, "bottom": 4.0}],
        "page_dimensions": [{"page_number": 1, "width": 612.0, "height": 792.0}],
    }
    document = {"id": "doc-001", "viewable_blob_uri": "https://blob.example.com/doc-001.pdf"}
    geometry_repos = ChunkGeometryRepos(
        chunk_repo=_StubChunkRepo(chunk=chunk), document_repo=_StubDocumentRepo(document=document)
    )

    result = await assemble_candidates(
        candidates, verdicts, DEFAULT_WEIGHTS, DEFAULT_SETTINGS, geometry_repos=geometry_repos
    )

    assert len(result) == 1
    link = result[0].provenance_links[0]
    assert link.preview_kind == "pdf"
    assert len(link.highlight_rects) == 1
    assert link.clean_excerpt


async def test_assemble_candidates_code_candidate_produces_code_preview_kind():
    candidates = [
        {
            "id": "cand-code",
            "batch_id": "batch-001",
            "document_id": "repo-1",
            "source_chunk_index": 4,
            "claim_text": "A code-derived method.",
            "title": "Invention Code",
            "description": "Description.",
            "source_span": {
                "locator": "chars:80-140",
                "source_kind": "code",
                "span_start": 80,
                "span_end": 140,
            },
        }
    ]
    verdicts = [
        {
            "candidate_id": "cand-code",
            "batch_id": "batch-001",
            "document_id": "repo-1",
            "axes": _all_verdict_axes(),
        }
    ]
    chunk = {
        "id": "repo-1|abcd1234|4",
        "text": "def train_step():\n    pass",
        "start_char": 80,
        "end_char": 140,
    }
    provenance_entry = {"file_path": "src/train.py", "line_range": [12, 20]}
    geometry_repos = ChunkGeometryRepos(
        chunk_repo=_StubChunkRepo(chunk=chunk),
        provenance_repo=_StubProvenanceRepo(entry=provenance_entry),
    )

    result = await assemble_candidates(
        candidates, verdicts, DEFAULT_WEIGHTS, DEFAULT_SETTINGS, geometry_repos=geometry_repos
    )

    assert len(result) == 1
    link = result[0].provenance_links[0]
    assert link.preview_kind == "code"
    assert link.highlight_rects is None
    assert link.file_path == "src/train.py"
    assert link.line_range.start_line == 12
    assert link.line_range.end_line == 20


async def test_assemble_candidates_chunk_read_failure_degrades_to_legacy_none_preview():
    candidates = [_pdf_candidate()]
    verdicts = [
        {
            "candidate_id": "cand-pdf",
            "batch_id": "batch-001",
            "document_id": "doc-001",
            "axes": _all_verdict_axes(),
        }
    ]
    geometry_repos = ChunkGeometryRepos(chunk_repo=_StubChunkRepo(error=RuntimeError("boom")))

    result = await assemble_candidates(
        candidates, verdicts, DEFAULT_WEIGHTS, DEFAULT_SETTINGS, geometry_repos=geometry_repos
    )

    assert len(result) == 1
    link = result[0].provenance_links[0]
    assert link.preview_kind == "none"
    assert link.highlight_rects is None


async def test_assemble_candidates_without_geometry_repos_defaults_to_none_preview():
    candidates = [_pdf_candidate()]
    verdicts = [
        {
            "candidate_id": "cand-pdf",
            "batch_id": "batch-001",
            "document_id": "doc-001",
            "axes": _all_verdict_axes(),
        }
    ]

    result = await assemble_candidates(candidates, verdicts, DEFAULT_WEIGHTS, DEFAULT_SETTINGS)

    assert len(result) == 1
    assert result[0].provenance_links[0].preview_kind == "none"
