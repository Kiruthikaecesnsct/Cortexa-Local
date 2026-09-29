import pytest

from seeding.application.parsing.claim_seed_parser import parse_claim_seed_set
from seeding.domain.enums.scoring_axis import ScoringAxis
from seeding.domain.errors.seeding_errors import ClaimSeedParseError
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


def _limitation(text: str = "a limitation", refs: list[str] | None = None) -> dict:
    return {"text": text, "evidence_refs": refs if refs is not None else [_VALID_REF]}


def _independent_claim(limitations: list[dict] | None = None) -> dict:
    return {
        "claim_type": "method",
        "preamble": "A method comprising",
        "recitations": ["recitation one"],
        "limitations": limitations if limitations is not None else [_limitation()],
    }


def _dependent_claim(parent_index: object = 0, added_limitations: list[dict] | None = None) -> dict:
    return {
        "parent_index": parent_index,
        "added_limitations": (
            added_limitations if added_limitations is not None else [_limitation()]
        ),
    }


def _full_raw() -> dict:
    return {
        "independent_claims": [_independent_claim()],
        "dependent_claims": [_dependent_claim(), _dependent_claim()],
    }


def test_evidence_ref_not_in_evidence_refs_raises_parse_error():
    raw = _full_raw()
    raw["independent_claims"] = [
        _independent_claim(limitations=[_limitation(refs=["[unknown:0-50]"])])
    ]

    with pytest.raises(ClaimSeedParseError):
        parse_claim_seed_set(raw, _make_candidate(), [_VALID_REF])


def test_out_of_range_parent_index_raises_parse_error():
    raw = _full_raw()
    raw["dependent_claims"] = [_dependent_claim(parent_index=5)]

    with pytest.raises(ClaimSeedParseError):
        parse_claim_seed_set(raw, _make_candidate(), [_VALID_REF])


def test_non_int_parent_index_raises_parse_error():
    raw = _full_raw()
    raw["dependent_claims"] = [_dependent_claim(parent_index="0")]

    with pytest.raises(ClaimSeedParseError):
        parse_claim_seed_set(raw, _make_candidate(), [_VALID_REF])


def test_missing_independent_claims_key_raises_parse_error():
    raw = _full_raw()
    del raw["independent_claims"]

    with pytest.raises(ClaimSeedParseError):
        parse_claim_seed_set(raw, _make_candidate(), [_VALID_REF])


def test_missing_dependent_claims_key_raises_parse_error():
    raw = _full_raw()
    del raw["dependent_claims"]

    with pytest.raises(ClaimSeedParseError):
        parse_claim_seed_set(raw, _make_candidate(), [_VALID_REF])


def test_invalid_claim_type_raises_parse_error():
    raw = _full_raw()
    raw["independent_claims"] = [{**_independent_claim(), "claim_type": "not-a-real-type"}]

    with pytest.raises(ClaimSeedParseError):
        parse_claim_seed_set(raw, _make_candidate(), [_VALID_REF])


def test_happy_path_returns_claim_seed_set():
    raw = _full_raw()

    seed_set = parse_claim_seed_set(raw, _make_candidate(), [_VALID_REF])

    assert seed_set.candidate_id == "cand-1"
    assert seed_set.batch_id == "batch-1"
    assert seed_set.document_id == "doc-1"
    assert len(seed_set.independent_claims) == 1
    assert len(seed_set.dependent_claims) == 2
    assert seed_set.independent_claims[0].claim_type == "method"
    assert seed_set.independent_claims[0].limitations[0].evidence_refs == [_VALID_REF]
    assert seed_set.dependent_claims[0].parent_index == 0
    assert seed_set.dependent_claims[0].added_limitations[0].evidence_refs == [_VALID_REF]
