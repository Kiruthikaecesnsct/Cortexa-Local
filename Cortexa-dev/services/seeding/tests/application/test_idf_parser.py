import pytest

from seeding.application.parsing.idf_parser import parse_idf_draft
from seeding.domain.enums.scoring_axis import ScoringAxis
from seeding.domain.errors.seeding_errors import AbstractTooLongError, IdfParseError
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


def _section(text: str, ref: str) -> dict:
    return {"text": text, "citations": [ref]}


def _full_raw(ref: str) -> dict:
    return {
        "abstract": _section("short abstract", ref),
        "background": _section("background text", ref),
        "summary": _section("summary text", ref),
        "core_differentiating_feature": _section("differentiating feature", ref),
    }


def test_missing_section_raises_parse_error():
    ref = "[src1:0-100]"
    raw = _full_raw(ref)
    del raw["summary"]

    with pytest.raises(IdfParseError):
        parse_idf_draft(raw, _make_candidate(), [ref])


def test_section_with_no_citations_raises_parse_error():
    ref = "[src1:0-100]"
    raw = _full_raw(ref)
    raw["background"] = {"text": "background text", "citations": []}

    with pytest.raises(IdfParseError):
        parse_idf_draft(raw, _make_candidate(), [ref])


def test_citation_not_in_evidence_refs_raises_parse_error():
    ref = "[src1:0-100]"
    raw = _full_raw(ref)
    raw["summary"] = _section("summary text", "[unknown:0-50]")

    with pytest.raises(IdfParseError):
        parse_idf_draft(raw, _make_candidate(), [ref])


def test_abstract_over_250_words_raises():
    ref = "[src1:0-100]"
    raw = _full_raw(ref)
    raw["abstract"] = _section("word " * 251, ref)

    with pytest.raises(AbstractTooLongError):
        parse_idf_draft(raw, _make_candidate(), [ref])


def test_happy_path_returns_draft():
    ref = "[src1:0-100]"
    raw = _full_raw(ref)

    draft = parse_idf_draft(raw, _make_candidate(), [ref])

    assert draft.candidate_id == "cand-1"
    assert draft.job_id == "job-1"
    assert draft.abstract.text == "short abstract"
    assert draft.background.citations == [ref]
    assert draft.summary.text == "summary text"
    assert draft.core_differentiating_feature.citations == [ref]
