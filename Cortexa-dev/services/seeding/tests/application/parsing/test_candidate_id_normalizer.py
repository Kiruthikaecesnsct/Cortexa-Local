import pytest

from seeding.application.parsing.candidate_id_normalizer import normalize_candidate_id

_UUID = "efae51f2-eba0-45ae-8a5d-3337a85811c9"


@pytest.mark.parametrize(
    "raw,expected",
    [
        (_UUID, _UUID),
        (f"{_UUID}]", _UUID),  # spurious trailing bracket (BUG161 case a)
        (f"[{_UUID}]", _UUID),
        (f'"{_UUID}",', _UUID),  # stray quotes + comma
        (f"  {_UUID}  ", _UUID),  # whitespace
        (_UUID.upper(), _UUID),  # case-insensitive canonicalization
        ("{efae51f2eba045ae8a5d3337a85811c9}", _UUID),  # hyphenless UUID
    ],
)
def test_recoverable_ids_canonicalize_to_uuid(raw: str, expected: str):
    assert normalize_candidate_id(raw) == expected


def test_non_uuid_string_is_cleaned_but_preserved():
    assert normalize_candidate_id("[cand-1]") == "cand-1"


def test_truncated_non_uuid_is_not_forced_into_uuid():
    truncated = "d98a83b2-5df8-b862-fdf698bcbf7d"
    assert normalize_candidate_id(truncated) == truncated


def test_none_and_empty_return_empty():
    assert normalize_candidate_id(None) == ""
    assert normalize_candidate_id("") == ""
