import pytest
from pydantic import ValidationError

from seeding.domain.enums.opportunity_category import OpportunityCategory
from seeding.domain.models.opportunity import Opportunity


def _make_opportunity(
    description: str = "valid description", justification: str = "valid justification"
) -> Opportunity:
    return Opportunity(
        category=next(iter(OpportunityCategory)),
        description=description,
        justification=justification,
        citations=["[src1:0-10]"],
    )


def test_accepts_clean_description_and_justification() -> None:
    opp = _make_opportunity()
    assert opp.description == "valid description"
    assert opp.justification == "valid justification"


@pytest.mark.parametrize("bad_value", ["bad\ntext", "bad\rtext", "bad==text", "bad--text"])
def test_rejects_newlines_and_delimiters_in_description(bad_value: str) -> None:
    with pytest.raises(ValidationError):
        _make_opportunity(description=bad_value)


@pytest.mark.parametrize("bad_value", ["bad\ntext", "bad\rtext", "bad==text", "bad--text"])
def test_rejects_newlines_and_delimiters_in_justification(bad_value: str) -> None:
    with pytest.raises(ValidationError):
        _make_opportunity(justification=bad_value)
