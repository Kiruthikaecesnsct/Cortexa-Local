import json

import pytest

from seeding.application.parsing.digest_parser import parse_map_note, parse_reduce_brief
from seeding.domain.errors.seeding_errors import DigestParseError

MAP_ALLOWED = {"c1", "c2"}


def _map_content(payload: dict) -> str:
    return json.dumps(payload)


def test_map_note_parses_valid_entries():
    content = _map_content({"claims_made": [{"text": "cuts memory", "chunk_ids": ["c1"]}]})
    note = parse_map_note(content, MAP_ALLOWED)
    assert len(note.claims_made) == 1
    assert note.claims_made[0].chunk_ids == ["c1"]
    assert not note.is_empty()


def test_map_note_tolerates_null_and_missing_lists():
    content = _map_content({"claims_made": None})
    note = parse_map_note(content, MAP_ALLOWED)
    assert note.is_empty()


def test_map_note_drops_ids_outside_allow_list():
    content = _map_content({"limitations": [{"text": "x", "chunk_ids": ["c1", "bogus"]}]})
    note = parse_map_note(content, MAP_ALLOWED)
    assert note.limitations[0].chunk_ids == ["c1"]


def test_map_note_drops_entry_with_only_unknown_ids():
    content = _map_content(
        {
            "limitations": [
                {"text": "kept", "chunk_ids": ["c1"]},
                {"text": "dropped", "chunk_ids": ["bogus"]},
            ]
        }
    )
    note = parse_map_note(content, MAP_ALLOWED)
    assert [e.text for e in note.limitations] == ["kept"]


def test_map_note_empty_is_valid():
    content = _map_content(
        {
            "claims_made": [],
            "methods_used": [],
            "limitations": [],
            "future_work": [],
            "key_concepts": [],
        }
    )
    note = parse_map_note(content, MAP_ALLOWED)
    assert note.is_empty()


def test_map_note_malformed_json_raises_digest_parse_error():
    with pytest.raises(DigestParseError):
        parse_map_note("not json at all", MAP_ALLOWED)


def test_map_note_per_item_isolation_skips_bad_entries():
    content = _map_content({"claims_made": ["not-a-dict", {"text": "good", "chunk_ids": ["c2"]}]})
    note = parse_map_note(content, MAP_ALLOWED)
    assert [e.text for e in note.claims_made] == ["good"]


def test_reduce_brief_unions_and_filters():
    content = _map_content(
        {
            "problem_space": {"text": "reduce memory cost", "chunk_ids": ["c1", "bad"]},
            "contributions": [{"text": "scheme", "chunk_ids": ["c2"]}],
        }
    )
    brief = parse_reduce_brief(content, MAP_ALLOWED)
    assert brief.problem_space.chunk_ids == ["c1"]
    assert brief.contributions[0].chunk_ids == ["c2"]
    assert not brief.is_empty()


def test_reduce_brief_drops_problem_space_text_without_citation():
    content = _map_content({"problem_space": {"text": "orphan", "chunk_ids": ["bogus"]}})
    brief = parse_reduce_brief(content, MAP_ALLOWED)
    assert brief.problem_space.text == ""
    assert brief.problem_space.chunk_ids == []


def test_reduce_brief_all_empty_is_empty():
    brief = parse_reduce_brief(_map_content({}), MAP_ALLOWED)
    assert brief.is_empty()


def test_reduce_brief_malformed_raises():
    with pytest.raises(DigestParseError):
        parse_reduce_brief("<<<", MAP_ALLOWED)
