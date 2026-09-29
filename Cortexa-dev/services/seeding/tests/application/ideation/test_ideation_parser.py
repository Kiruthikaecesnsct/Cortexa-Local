import json

from seeding.application.parsing.ideation_parser import (
    parse_critique,
    parse_propose,
    parse_refine,
)
from seeding.domain.enums.opportunity_category import OpportunityCategory
from seeding.domain.models.ideation import IdeaSketch

ALLOWED_CHUNK_IDS = {"chunk-1", "chunk-2", "chunk-3"}
IDEAS_PER_ROUND = 5


def _propose_payload(ideas: list) -> str:
    return json.dumps({"ideas": ideas})


def _critique_payload(verdicts: list) -> str:
    return json.dumps({"verdicts": verdicts})


def _refine_payload(opportunities: list) -> str:
    return json.dumps({"opportunities": opportunities})


def _valid_sketch(sketch_id: str = "s1", chunk_ids: list[str] | None = None) -> dict:
    return {
        "sketch_id": sketch_id,
        "title": "A new whitespace mechanism",
        "summary": "Extends the asset with a novel adaptive step.",
        "chunk_ids": chunk_ids or ["chunk-1"],
        "novelty_delta": "Adds an adaptive feedback loop absent from the document.",
        "target_concept": "adaptive feedback",
    }


def test_parse_propose_one_malformed_element_two_survive_with_drop_reason():
    ideas = [_valid_sketch("s1"), "not-an-object", _valid_sketch("s2", ["chunk-2"])]

    result = parse_propose(_propose_payload(ideas), ALLOWED_CHUNK_IDS, IDEAS_PER_ROUND)

    assert [s.sketch_id for s in result.sketches] == ["s1", "s2"]
    assert len(result.dropped) == 1
    assert "sketch[1]" in result.dropped[0]


def test_parse_propose_unknown_chunk_id_dropped_valid_siblings_survive():
    ideas = [
        _valid_sketch("s1", ["chunk-unknown"]),
        _valid_sketch("s2", ["chunk-1", "chunk-unknown"]),
    ]

    result = parse_propose(_propose_payload(ideas), ALLOWED_CHUNK_IDS, IDEAS_PER_ROUND)

    assert [s.sketch_id for s in result.sketches] == ["s2"]
    assert result.sketches[0].chunk_ids == ["chunk-1"]
    assert any("sketch[0]" in reason for reason in result.dropped)


def test_parse_propose_idea_with_zero_valid_chunk_ids_dropped():
    ideas = [_valid_sketch("s1", ["chunk-unknown-a", "chunk-unknown-b"])]

    result = parse_propose(_propose_payload(ideas), ALLOWED_CHUNK_IDS, IDEAS_PER_ROUND)

    assert result.sketches == []
    assert "no valid chunk_ids" in result.dropped[0]


def test_parse_propose_empty_payload_returns_empty_result_no_exception():
    result = parse_propose(_propose_payload([]), ALLOWED_CHUNK_IDS, IDEAS_PER_ROUND)

    assert result.sketches == []
    assert result.dropped == []


def test_parse_propose_garbage_content_returns_empty_result_no_exception():
    result = parse_propose("not json at all, no braces here", ALLOWED_CHUNK_IDS, IDEAS_PER_ROUND)

    assert result.sketches == []
    assert len(result.dropped) == 1
    assert "extract_failed" in result.dropped[0]


def test_parse_propose_non_array_ideas_field_returns_empty_result_no_exception():
    content = json.dumps({"ideas": "not-a-list"})

    result = parse_propose(content, ALLOWED_CHUNK_IDS, IDEAS_PER_ROUND)

    assert result.sketches == []
    assert "missing_or_non_array" in result.dropped[0]


def test_parse_critique_invalid_verdict_value_fails_closed():
    sketch_ids = {"s1"}
    verdicts = [{"sketch_id": "s1", "verdict": "maybe", "reason_code": "accept"}]

    result = parse_critique(_critique_payload(verdicts), sketch_ids)

    assert result.accepted_ids == set()
    assert result.rejections == []
    assert len(result.dropped) == 1


def test_parse_critique_missing_verdict_field_fails_closed():
    sketch_ids = {"s1"}
    verdicts = [{"sketch_id": "s1", "reason_code": "accept"}]

    result = parse_critique(_critique_payload(verdicts), sketch_ids)

    assert result.accepted_ids == set()
    assert len(result.dropped) == 1


def test_parse_critique_accept_with_reject_reason_code_fails_closed():
    sketch_ids = {"s1"}
    verdicts = [{"sketch_id": "s1", "verdict": "accept", "reason_code": "restates_document"}]

    result = parse_critique(_critique_payload(verdicts), sketch_ids)

    assert result.accepted_ids == set()
    assert len(result.dropped) == 1


def test_parse_critique_unknown_sketch_id_dropped():
    sketch_ids = {"s1"}
    verdicts = [{"sketch_id": "s-unknown", "verdict": "accept", "reason_code": "accept"}]

    result = parse_critique(_critique_payload(verdicts), sketch_ids)

    assert result.accepted_ids == set()
    assert "unknown sketch_id" in result.dropped[0]


def test_parse_critique_valid_accept_and_reject_both_recorded():
    sketch_ids = {"s1", "s2"}
    verdicts = [
        {"sketch_id": "s1", "verdict": "accept", "reason_code": "accept"},
        {"sketch_id": "s2", "verdict": "reject", "reason_code": "restates_document"},
    ]

    result = parse_critique(_critique_payload(verdicts), sketch_ids)

    assert result.accepted_ids == {"s1"}
    assert result.rejections == [("s2", "restates_document")]
    assert result.dropped == []


def _refine_sketch_map() -> dict[str, IdeaSketch]:
    sketch = IdeaSketch(
        sketch_id="s1",
        title="A new mechanism",
        summary="summary",
        chunk_ids=["chunk-1", "chunk-2"],
        novelty_delta="delta",
        target_concept="concept",
    )
    return {"s1": sketch}


def _valid_refined(sketch_id: str = "s1", chunk_ids: list[str] | None = None) -> dict:
    return {
        "sketch_id": sketch_id,
        "title": "Refined title",
        "description": "Refined description of the opportunity.",
        "mechanism": "Refined mechanism detail.",
        "claim_statement": "A method comprising...",
        "category": OpportunityCategory.Whitespace.value,
        "novelty_delta": "New delta",
        "chunk_ids": chunk_ids or ["chunk-1"],
        "roadmap_alignment": "aligns with roadmap item X",
    }


def test_parse_refine_roadmap_alignment_dropped_when_roadmap_not_supplied():
    sketches = _refine_sketch_map()

    result = parse_refine(_refine_payload([_valid_refined()]), sketches, roadmap_supplied=False)

    assert len(result.opportunities) == 1
    assert result.opportunities[0].roadmap_alignment == ""


def test_parse_refine_roadmap_alignment_kept_when_roadmap_supplied():
    sketches = _refine_sketch_map()

    result = parse_refine(_refine_payload([_valid_refined()]), sketches, roadmap_supplied=True)

    assert result.opportunities[0].roadmap_alignment == "aligns with roadmap item X"


def test_parse_refine_unknown_sketch_id_dropped():
    sketches = _refine_sketch_map()
    payload = _refine_payload([_valid_refined(sketch_id="s-unknown")])

    result = parse_refine(payload, sketches, roadmap_supplied=False)

    assert result.opportunities == []
    assert "unknown sketch_id" in result.dropped[0]


def test_parse_refine_chunk_ids_outside_sketch_scope_dropped():
    sketches = _refine_sketch_map()
    payload = _refine_payload([_valid_refined(chunk_ids=["chunk-outside-scope"])])

    result = parse_refine(payload, sketches, roadmap_supplied=False)

    assert result.opportunities == []
    assert "no valid chunk_ids" in result.dropped[0]


def test_parse_refine_empty_payload_returns_empty_result_no_exception():
    sketches = _refine_sketch_map()

    result = parse_refine(_refine_payload([]), sketches, roadmap_supplied=False)

    assert result.opportunities == []
    assert result.dropped == []
