import pytest

from seeding.application.parsing.lattice_parser import parse_lattice
from seeding.domain.enums.scoring_axis import ScoringAxis
from seeding.domain.errors.seeding_errors import LatticeParseError
from seeding.domain.models.axis_score import AxisScore
from seeding.domain.models.scored_candidate import ScoredCandidate

_VALID_REF = "[src1:0-100]"


def _make_candidate() -> ScoredCandidate:
    return ScoredCandidate(
        candidate_id="cand-1",
        batch_id="batch-1",
        job_id="job-1",
        document_id="doc-1",
        axes={axis: AxisScore(axis=axis, score=75, refs=[]) for axis in ScoringAxis},
    )


def _entry(refs: list[str] | None = None, **overrides) -> dict:
    base = {
        "title": "An entry",
        "description": "description",
        "scope": "scope",
        "filing_strategy_note": "note",
        "evidence_refs": refs if refs is not None else [_VALID_REF],
    }
    base.update(overrides)
    return base


def _full_raw() -> dict:
    return {
        "core": _entry(),
        "continuations": [_entry(), _entry()],
        "platform": [_entry(), _entry()],
        "system": [_entry(), _entry()],
    }


def test_happy_path_returns_invention_lattice():
    raw = _full_raw()

    lattice = parse_lattice(raw, _make_candidate(), [_VALID_REF])

    assert lattice.candidate_id == "cand-1"
    assert lattice.batch_id == "batch-1"
    assert lattice.document_id == "doc-1"
    assert lattice.core.title == "An entry"
    assert len(lattice.continuations) == 2
    assert len(lattice.platform) == 2
    assert len(lattice.system) == 2


def test_missing_core_raises_parse_error():
    raw = _full_raw()
    del raw["core"]

    with pytest.raises(LatticeParseError):
        parse_lattice(raw, _make_candidate(), [_VALID_REF])


def test_missing_continuations_raises_parse_error():
    raw = _full_raw()
    del raw["continuations"]

    with pytest.raises(LatticeParseError):
        parse_lattice(raw, _make_candidate(), [_VALID_REF])


def test_missing_platform_raises_parse_error():
    raw = _full_raw()
    del raw["platform"]

    with pytest.raises(LatticeParseError):
        parse_lattice(raw, _make_candidate(), [_VALID_REF])


def test_missing_system_raises_parse_error():
    raw = _full_raw()
    del raw["system"]

    with pytest.raises(LatticeParseError):
        parse_lattice(raw, _make_candidate(), [_VALID_REF])


def test_entry_without_evidence_refs_raises_parse_error():
    raw = _full_raw()
    raw["core"] = _entry(refs=[])

    with pytest.raises(LatticeParseError):
        parse_lattice(raw, _make_candidate(), [_VALID_REF])


def test_unknown_evidence_ref_raises_parse_error():
    raw = _full_raw()
    raw["continuations"] = [_entry(refs=["[unknown:0-50]"]), _entry()]

    with pytest.raises(LatticeParseError):
        parse_lattice(raw, _make_candidate(), [_VALID_REF])


def test_entry_with_non_string_title_raises_parse_error():
    raw = _full_raw()
    bad_entry = _entry(title={"nested": "object"})
    raw["platform"] = [bad_entry, _entry()]

    with pytest.raises(LatticeParseError):
        parse_lattice(raw, _make_candidate(), [_VALID_REF])
