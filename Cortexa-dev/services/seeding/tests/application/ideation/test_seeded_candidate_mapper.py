import re

from seeding.application.ideation.seeded_candidate_mapper import (
    map_accepted_idea,
    map_accepted_ideas,
)
from seeding.domain.models.ideation import RetrievedExcerpt
from seeding.domain.models.scratchpad import AcceptedIdea

BATCH_ID = "batch-77"
DOC_ID = "doc-a/b|c.txt"
_COSMOS_SAFE = re.compile(r"^seed-[0-9a-f]{64}$")


def _idea(**overrides) -> AcceptedIdea:
    base = {
        "title": "Adaptive gradient checkpointing",
        "summary": "Existing training wastes memory.",
        "novelty_delta": "reduces peak memory 40%",
        "chunk_ids": ["c1", "c2"],
        "description": "A scheme that recomputes activations.",
        "mechanism": "selective activation recomputation",
        "claim_statement": "A method comprising recomputing activations.",
        "category": "Whitespace",
        "roadmap_alignment": "edge training",
        "target_concept": "memory efficiency",
        "round_index": 2,
        "excerpts": [RetrievedExcerpt(chunk_id="c1", section_label="Method", text="recompute")],
    }
    base.update(overrides)
    return AcceptedIdea(**base)


def test_mapper_maps_fields_per_candidate_shape():
    doc = map_accepted_idea(_idea(), BATCH_ID, DOC_ID)

    assert doc["claim_text"] == "A method comprising recomputing activations."
    assert doc["problem"] == "Existing training wastes memory."
    assert doc["mechanism"] == "selective activation recomputation"
    assert doc["batch_id"] == BATCH_ID
    assert doc["document_id"] == DOC_ID
    assert doc["engine"] == "seeding"
    assert doc["title"] == "Adaptive gradient checkpointing"
    assert doc["category"] == "Whitespace"
    assert doc["novelty_delta"] == "reduces peak memory 40%"
    assert doc["roadmap_alignment"] == "edge training"
    assert doc["round_index"] == 2


def test_problem_falls_back_to_target_concept_when_summary_blank():
    doc = map_accepted_idea(_idea(summary=""), BATCH_ID, DOC_ID)
    assert doc["problem"] == "memory efficiency"


def test_description_falls_back_to_summary_when_blank():
    doc = map_accepted_idea(_idea(description=""), BATCH_ID, DOC_ID)
    assert doc["description"] == "Existing training wastes memory."


def test_target_concept_is_persisted_as_top_level_field():
    doc = map_accepted_idea(_idea(), BATCH_ID, DOC_ID)
    assert doc["target_concept"] == "memory efficiency"


def test_target_concept_still_folds_into_problem_when_summary_blank():
    doc = map_accepted_idea(_idea(summary=""), BATCH_ID, DOC_ID)
    assert doc["problem"] == "memory efficiency"
    assert doc["target_concept"] == "memory efficiency"


def test_provenance_carries_chunk_ids_and_excerpts():
    doc = map_accepted_idea(_idea(), BATCH_ID, DOC_ID)
    assert doc["provenance"]["chunk_ids"] == ["c1", "c2"]
    assert doc["provenance"]["excerpts"][0]["chunk_id"] == "c1"


def test_id_is_deterministic_for_same_inputs():
    first = map_accepted_idea(_idea(), BATCH_ID, DOC_ID)
    second = map_accepted_idea(_idea(), BATCH_ID, DOC_ID)
    assert first["id"] == second["id"]


def test_id_changes_with_round_index_and_title():
    base = map_accepted_idea(_idea(), BATCH_ID, DOC_ID)
    other_round = map_accepted_idea(_idea(round_index=3), BATCH_ID, DOC_ID)
    other_title = map_accepted_idea(_idea(title="Different"), BATCH_ID, DOC_ID)
    assert base["id"] != other_round["id"]
    assert base["id"] != other_title["id"]


def test_id_is_cosmos_safe_hash_only_no_raw_text_or_paths():
    doc = map_accepted_idea(_idea(), BATCH_ID, DOC_ID)
    candidate_id = doc["id"]
    assert _COSMOS_SAFE.match(candidate_id)
    for illegal in ("/", "\\", "?", "#", " "):
        assert illegal not in candidate_id
    assert DOC_ID not in candidate_id


def test_map_many_returns_one_doc_per_idea():
    docs = map_accepted_ideas([_idea(), _idea(round_index=5)], BATCH_ID, DOC_ID)
    assert len(docs) == 2
    assert docs[0]["id"] != docs[1]["id"]
