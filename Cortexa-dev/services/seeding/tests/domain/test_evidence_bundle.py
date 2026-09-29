import pytest
from pydantic import ValidationError

from seeding.domain.models.evidence_bundle import EvidenceHit


def test_evidence_hit_accepts_clean_hit() -> None:
    hit = EvidenceHit(source_id="patent-123", text="clean text", start=0, end=10)

    assert hit.source_id == "patent-123"
    assert hit.text == "clean text"
    assert hit.start == 0
    assert hit.end == 10


@pytest.mark.parametrize("bad_value", ["a\nb", "a\rb", "a==b", "a--b"])
def test_evidence_hit_rejects_prompt_delimiters_in_text(bad_value: str) -> None:
    with pytest.raises(ValidationError):
        EvidenceHit(source_id="patent-123", text=bad_value, start=0, end=10)


@pytest.mark.parametrize("bad_value", ["a\nb", "a\rb", "a==b", "a--b"])
def test_evidence_hit_rejects_prompt_delimiters_in_source_id(bad_value: str) -> None:
    with pytest.raises(ValidationError):
        EvidenceHit(source_id=bad_value, text="clean text", start=0, end=10)
