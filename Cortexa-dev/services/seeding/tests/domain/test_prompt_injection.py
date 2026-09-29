import pytest

from seeding.domain.validation.prompt_injection import (
    contains_prompt_delimiter,
    reject_prompt_delimiters,
)


@pytest.mark.parametrize("bad_value", ["a\nb", "a\rb", "a==b", "a--b"])
def test_contains_prompt_delimiter_detects_each_delimiter(bad_value: str) -> None:
    assert contains_prompt_delimiter(bad_value) is True


def test_contains_prompt_delimiter_false_for_clean_text() -> None:
    assert contains_prompt_delimiter("clean text") is False


def test_reject_prompt_delimiters_passes_through_clean_strings() -> None:
    assert reject_prompt_delimiters("clean text") == "clean text"


def test_reject_prompt_delimiters_passes_through_non_strings() -> None:
    assert reject_prompt_delimiters(123) == 123


@pytest.mark.parametrize("bad_value", ["a\nb", "a\rb", "a==b", "a--b"])
def test_reject_prompt_delimiters_raises_on_each_delimiter(bad_value: str) -> None:
    with pytest.raises(ValueError):
        reject_prompt_delimiters(bad_value)
