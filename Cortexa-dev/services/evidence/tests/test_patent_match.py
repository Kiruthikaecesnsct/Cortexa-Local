import pytest
from pydantic import ValidationError

from evidence.domain.enums.evidence_source import EvidenceSource
from evidence.domain.models.patent_match import PatentMatch


def _make(**kwargs) -> PatentMatch:
    base = dict(
        reference="US12345678",
        title="Test patent",
        applicant="Test Corp",
        date="2024-01-01",
        url="https://example.com",
        relevance_score=0.5,
    )
    base.update(kwargs)
    return PatentMatch(**base)


def test_defaults_source_to_patent_api():
    match = _make()
    assert match.source is EvidenceSource.PatentApi


def test_valid_score_boundaries():
    assert _make(relevance_score=0.0).relevance_score == 0.0
    assert _make(relevance_score=1.0).relevance_score == 1.0


def test_score_below_zero_raises():
    with pytest.raises(ValidationError):
        _make(relevance_score=-0.01)


def test_score_above_one_raises():
    with pytest.raises(ValidationError):
        _make(relevance_score=1.01)


def test_all_fields_required():
    with pytest.raises(ValidationError):
        PatentMatch(reference="US1", title="T", applicant="A", date="2024", url="http://x")
