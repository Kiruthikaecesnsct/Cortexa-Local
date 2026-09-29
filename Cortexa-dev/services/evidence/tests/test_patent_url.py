import pytest

from evidence.domain.services.patent_url import canonical_google_patents_url


def test_hyphenated_us_pregrant_publication_zero_padded():
    result = canonical_google_patents_url("US-2021345925-A1")
    assert result == "https://patents.google.com/patent/US20210345925A1"


def test_granted_us_patent_number_strips_separators_no_padding():
    result = canonical_google_patents_url("US-10123456-B2")
    assert result == "https://patents.google.com/patent/US10123456B2"


def test_already_canonical_input_is_idempotent():
    canonical_input = "US20210345925A1"
    result = canonical_google_patents_url(canonical_input)
    assert result == "https://patents.google.com/patent/US20210345925A1"


def test_function_output_is_stable_when_rerun_on_normalized_number():
    first_pass = canonical_google_patents_url("US-2021345925-A1")
    normalized_number = first_pass.replace("https://patents.google.com/patent/", "")
    second_pass = canonical_google_patents_url(normalized_number)
    assert first_pass == second_pass


def test_non_us_patent_numbers_stripped_but_not_reinterpreted():
    ep_result = canonical_google_patents_url("EP-1234567-A1")
    assert ep_result == "https://patents.google.com/patent/EP1234567A1"

    wo_result = canonical_google_patents_url("WO-2020123456-A1")
    assert wo_result == "https://patents.google.com/patent/WO2020123456A1"


def test_empty_input_returns_empty_string():
    assert canonical_google_patents_url("") == ""


def test_blank_input_returns_empty_string():
    assert canonical_google_patents_url("   ") == ""


def test_whitespace_only_input_returns_empty_string():
    assert canonical_google_patents_url("\t\n  ") == ""


def test_kindless_granted_us_number_not_fabricated():
    result_with_country = canonical_google_patents_url("US10123456")
    assert result_with_country == "https://patents.google.com/patent/US10123456"


def test_bare_number_without_country_code_returns_empty():
    result = canonical_google_patents_url("10123456")
    assert result == ""


def test_already_padded_pregrant_body_not_double_padded():
    already_eleven_digits = "US20210345925A1"
    result = canonical_google_patents_url(already_eleven_digits)
    normalized = result.replace("https://patents.google.com/patent/", "")

    body = normalized[2:13]
    assert len(body) == 11
    assert normalized == "US20210345925A1"


@pytest.mark.parametrize(
    "input_ref,expected_url",
    [
        ("US-2021-345925-A1", "https://patents.google.com/patent/US20210345925A1"),
        ("US 2021 345925 A1", "https://patents.google.com/patent/US20210345925A1"),
        ("US/2021/345925/A1", "https://patents.google.com/patent/US20210345925A1"),
        ("us-2021345925-a1", "https://patents.google.com/patent/US20210345925A1"),
        ("US-2021-12345-A1", "https://patents.google.com/patent/US20210012345A1"),
        ("US-2021-1-A1", "https://patents.google.com/patent/US20210000001A1"),
    ],
)
def test_us_pregrant_normalization_variants(input_ref, expected_url):
    result = canonical_google_patents_url(input_ref)
    assert result == expected_url


@pytest.mark.parametrize(
    "input_ref,expected_url",
    [
        ("US-10123456-B2", "https://patents.google.com/patent/US10123456B2"),
        ("US 10123456 B2", "https://patents.google.com/patent/US10123456B2"),
        ("US/10123456/B2", "https://patents.google.com/patent/US10123456B2"),
        ("us-10123456-b2", "https://patents.google.com/patent/US10123456B2"),
        ("US10123456B2", "https://patents.google.com/patent/US10123456B2"),
    ],
)
def test_us_granted_normalization_variants(input_ref, expected_url):
    result = canonical_google_patents_url(input_ref)
    assert result == expected_url
