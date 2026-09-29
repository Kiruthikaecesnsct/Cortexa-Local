import pytest

from seeding.application.parsing.seeding_opportunity_parser import (
    ParsedOpportunities,
    parse_seeding_opportunities,
)
from seeding.domain.errors.seeding_errors import OpportunityParseError

CANDIDATE_IDS = {"cand-1", "cand-2"}


def _make_raw_item(
    *,
    category: str = "Whitespace",
    title: str = "Test Title",
    description: str = "Test desc",
    innovation_rationale: str = "Novel",
    roadmap_integration: str = "Q3",
    confidence: float = 0.8,
    source_candidate_ids: list | None = None,
) -> dict:
    return {
        "category": category,
        "title": title,
        "description": description,
        "innovation_rationale": innovation_rationale,
        "roadmap_integration": roadmap_integration,
        "confidence": confidence,
        "source_candidate_ids": source_candidate_ids
        if source_candidate_ids is not None
        else ["cand-1"],
    }


def _make_raw(items: list[dict]) -> dict:
    return {"opportunities": items}


def test_valid_single_item_returns_correct_opportunity():
    raw = _make_raw([_make_raw_item(confidence=0.75)])

    result = parse_seeding_opportunities(raw, CANDIDATE_IDS)

    assert isinstance(result, ParsedOpportunities)
    assert len(result.opportunities) == 1
    assert len(result.dropped) == 0
    opp = result.opportunities[0]
    assert opp.title == "Test Title"
    assert opp.description == "Test desc"
    assert opp.confidence_score == 0.75
    assert opp.source_candidate_ids == ["cand-1"]
    assert opp.innovation_rationale == "Novel"
    assert opp.roadmap_alignment == "Q3"


@pytest.mark.parametrize(
    "raw_confidence,expected",
    [
        (-0.5, 0.0),
        (0.0, 0.0),
        (0.5, 0.5),
        (1.0, 1.0),
        (1.5, 1.0),
    ],
)
def test_confidence_is_clamped(raw_confidence: float, expected: float):
    raw = _make_raw([_make_raw_item(confidence=raw_confidence)])

    result = parse_seeding_opportunities(raw, CANDIDATE_IDS)

    assert result.opportunities[0].confidence_score == expected


def test_missing_opportunities_key_raises_parse_error():
    with pytest.raises(OpportunityParseError):
        parse_seeding_opportunities({}, CANDIDATE_IDS)


def test_empty_opportunities_list_raises_parse_error():
    with pytest.raises(OpportunityParseError):
        parse_seeding_opportunities({"opportunities": []}, CANDIDATE_IDS)


def test_empty_source_candidate_ids_raises_parse_error():
    raw = _make_raw([_make_raw_item(source_candidate_ids=[])])

    with pytest.raises(OpportunityParseError, match="no source_candidate_ids"):
        parse_seeding_opportunities(raw, CANDIDATE_IDS)


def test_unknown_source_candidate_id_raises_parse_error():
    raw = _make_raw([_make_raw_item(source_candidate_ids=["unknown-id"])])

    with pytest.raises(OpportunityParseError, match="unknown candidate ids"):
        parse_seeding_opportunities(raw, CANDIDATE_IDS)


_UUID = "efae51f2-eba0-45ae-8a5d-3337a85811c9"


def test_malformed_id_with_trailing_bracket_is_recovered():
    raw = _make_raw([_make_raw_item(source_candidate_ids=[f"{_UUID}]"])])

    result = parse_seeding_opportunities(raw, {_UUID})

    assert len(result.opportunities) == 1
    assert result.opportunities[0].source_candidate_ids == [_UUID]


def test_cross_chunk_id_validates_against_batch_wide_set():
    # id absent from the prompt chunk but present in the batch candidate set
    raw = _make_raw([_make_raw_item(source_candidate_ids=[_UUID])])

    result = parse_seeding_opportunities(raw, {_UUID, "cand-1"})

    assert len(result.opportunities) == 1


def test_opportunity_with_mixed_ids_keeps_only_the_valid_ones():
    raw = _make_raw([_make_raw_item(source_candidate_ids=["cand-1", "garbage-999", f"[{_UUID}]"])])

    result = parse_seeding_opportunities(raw, {"cand-1", _UUID})

    assert len(result.opportunities) == 1
    assert result.opportunities[0].source_candidate_ids == ["cand-1", _UUID]


def test_opportunity_grounded_on_only_unknown_ids_is_dropped():
    raw = _make_raw(
        [
            _make_raw_item(title="Grounded", source_candidate_ids=["cand-1"]),
            _make_raw_item(title="Ungrounded", source_candidate_ids=["garbage-1"]),
        ]
    )

    result = parse_seeding_opportunities(raw, CANDIDATE_IDS)

    assert len(result.opportunities) == 1
    assert result.opportunities[0].title == "Grounded"
    assert any("unknown candidate ids" in drop for drop in result.dropped)


def test_unknown_category_raises_parse_error():
    raw = _make_raw([_make_raw_item(category="InvalidCategory")])

    with pytest.raises(OpportunityParseError, match="unknown category"):
        parse_seeding_opportunities(raw, CANDIDATE_IDS)


def test_multiple_items_returns_all_parsed():
    items = [
        _make_raw_item(source_candidate_ids=["cand-1"]),
        _make_raw_item(source_candidate_ids=["cand-2"]),
    ]

    result = parse_seeding_opportunities(_make_raw(items), CANDIDATE_IDS)

    assert len(result.opportunities) == 2
    assert len(result.dropped) == 0


def test_mixed_valid_and_invalid_items_keeps_valid_and_drops_invalid():
    items = [
        _make_raw_item(title="Valid 1", source_candidate_ids=["cand-1"]),
        _make_raw_item(title="Invalid", source_candidate_ids=["cand-999"]),
        _make_raw_item(title="Valid 2", source_candidate_ids=["cand-2"]),
        _make_raw_item(title="Also Invalid", category="UnknownCategory"),
    ]

    result = parse_seeding_opportunities(_make_raw(items), CANDIDATE_IDS)

    assert len(result.opportunities) == 2
    assert result.opportunities[0].title == "Valid 1"
    assert result.opportunities[1].title == "Valid 2"
    assert len(result.dropped) == 2
    assert any("unknown candidate ids" in drop for drop in result.dropped)
    assert any("unknown category" in drop for drop in result.dropped)


@pytest.mark.parametrize(
    "bad_value", ["Q3\ninjection", "Q3\rinjection", "Q3==attack", "Q3--attack"]
)
def test_roadmap_alignment_rejects_prompt_delimiters(bad_value: str):
    from pydantic import ValidationError

    raw = _make_raw([_make_raw_item(roadmap_integration=bad_value)])

    with pytest.raises(ValidationError, match="newlines|==|--"):
        parse_seeding_opportunities(raw, CANDIDATE_IDS)


def test_roadmap_integration_model_key_maps_to_roadmap_alignment_domain_field():
    raw = _make_raw([_make_raw_item(roadmap_integration="Q3 2026 release")])

    result = parse_seeding_opportunities(raw, CANDIDATE_IDS)

    assert len(result.opportunities) == 1
    assert result.opportunities[0].roadmap_alignment == "Q3 2026 release"
