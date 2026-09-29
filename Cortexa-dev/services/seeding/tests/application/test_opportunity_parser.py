import pytest

from seeding.application.parsing.opportunity_parser import parse_opportunity_map
from seeding.domain.enums.opportunity_category import OpportunityCategory
from seeding.domain.enums.scoring_axis import ScoringAxis
from seeding.domain.errors.seeding_errors import OpportunityParseError
from seeding.domain.models.axis_score import AxisScore
from seeding.domain.models.scored_candidate import ScoredCandidate


def _make_candidate() -> ScoredCandidate:
    return ScoredCandidate(
        candidate_id="cand-1",
        batch_id="batch-1",
        job_id="job-1",
        document_id="doc-1",
        axes={axis: AxisScore(axis=axis, score=75, refs=[]) for axis in ScoringAxis},
    )


def _valid_opp(ref: str) -> dict:
    return {"description": "d", "justification": "j", "citations": [ref]}


def _full_raw(ref: str) -> dict:
    opp = _valid_opp(ref)
    return {
        "Whitespace": [opp],
        "Defensive": [opp],
        "Adjacent": [opp],
        "Continuation": [opp],
    }


def test_missing_category_raises_parse_error():
    evidence_refs = ["[src1:0-100]"]
    raw = {
        "Whitespace": [_valid_opp(evidence_refs[0])],
        "Defensive": [_valid_opp(evidence_refs[0])],
        "Adjacent": [_valid_opp(evidence_refs[0])],
    }
    with pytest.raises(OpportunityParseError):
        parse_opportunity_map(raw, _make_candidate(), evidence_refs)


def test_opportunity_with_no_citations_raises_parse_error():
    evidence_refs = ["[src1:0-100]"]
    raw = _full_raw(evidence_refs[0])
    raw["Whitespace"] = [{"description": "d", "justification": "j", "citations": []}]

    with pytest.raises(OpportunityParseError):
        parse_opportunity_map(raw, _make_candidate(), evidence_refs)


def test_citation_not_in_evidence_refs_raises_parse_error():
    evidence_refs = ["[src1:0-100]"]
    raw = _full_raw(evidence_refs[0])
    raw["Whitespace"] = [_valid_opp("[unknown:0-50]")]

    with pytest.raises(OpportunityParseError):
        parse_opportunity_map(raw, _make_candidate(), evidence_refs)


def test_all_four_categories_valid_returns_map():
    evidence_refs = ["[src1:0-100]"]
    raw = _full_raw(evidence_refs[0])

    result = parse_opportunity_map(raw, _make_candidate(), evidence_refs)

    assert set(result.opportunities.keys()) == set(OpportunityCategory)
    for opps in result.opportunities.values():
        assert len(opps) >= 1
