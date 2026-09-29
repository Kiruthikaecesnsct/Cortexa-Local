import pytest

from evidence.infrastructure.corpus.bigquery.bigquery_row_mapper import (
    map_row,
    map_rows,
)


@pytest.fixture
def base_valid_row():
    return {
        "publication_number": "US-10123456-B2",
        "country_code": "US",
        "kind_code": "B2",
        "application_number": "15/789123",
        "family_id": "67890123",
        "publication_date": "20180115",
        "filing_date": "20161220",
        "title": "Method and system for patent analysis",
        "abstract": "A novel approach to analyzing patent data using machine learning techniques.",
        "assignee": "Acme Corporation",
        "cpc_codes": "G06F 16/93; G06N 20/00",
    }


def test_map_row_with_valid_full_row(base_valid_row):
    record = map_row(base_valid_row)

    assert record is not None
    assert record.reference == "US-10123456-B2"
    assert record.title == "Method and system for patent analysis"
    assert (
        record.abstract
        == "A novel approach to analyzing patent data using machine learning techniques."
    )
    assert record.applicant == "Acme Corporation"
    assert record.date == "2018-01-15"
    assert record.url == "https://patents.google.com/patent/US10123456B2"
    assert record.metadata["country_code"] == "US"
    assert record.metadata["kind_code"] == "B2"
    assert record.metadata["application_number"] == "15/789123"
    assert record.metadata["family_id"] == "67890123"
    assert record.metadata["cpc_codes"] == "G06F 16/93; G06N 20/00"
    assert isinstance(record.metadata["cpc_codes"], str)
    assert record.metadata["filing_date"] == "2016-12-20"
    assert record.embedding_text() == (
        "Method and system for patent analysis. "
        "A novel approach to analyzing patent data using machine learning techniques."
    )


def test_map_row_skips_row_with_missing_publication_number(base_valid_row):
    base_valid_row.pop("publication_number")

    record = map_row(base_valid_row)

    assert record is None


def test_map_row_skips_row_with_empty_publication_number(base_valid_row):
    base_valid_row["publication_number"] = ""

    record = map_row(base_valid_row)

    assert record is None


def test_map_row_skips_row_with_whitespace_only_publication_number(base_valid_row):
    base_valid_row["publication_number"] = "   "

    record = map_row(base_valid_row)

    assert record is None


def test_map_row_skips_row_when_both_title_and_abstract_empty(base_valid_row):
    base_valid_row["title"] = ""
    base_valid_row["abstract"] = ""

    record = map_row(base_valid_row)

    assert record is None


def test_map_row_skips_row_when_both_title_and_abstract_missing(base_valid_row):
    base_valid_row.pop("title")
    base_valid_row.pop("abstract")

    record = map_row(base_valid_row)

    assert record is None


def test_map_row_creates_record_when_title_present_abstract_empty(base_valid_row):
    base_valid_row["abstract"] = ""

    record = map_row(base_valid_row)

    assert record is not None
    assert record.title == "Method and system for patent analysis"
    assert record.abstract == ""


def test_map_row_creates_record_when_abstract_present_title_empty(base_valid_row):
    base_valid_row["title"] = ""

    record = map_row(base_valid_row)

    assert record is not None
    assert record.title == ""
    assert (
        record.abstract
        == "A novel approach to analyzing patent data using machine learning techniques."
    )


def test_map_row_handles_bad_publication_date_short(base_valid_row):
    base_valid_row["publication_date"] = "2018"

    record = map_row(base_valid_row)

    assert record is not None
    assert record.date == ""


def test_map_row_handles_bad_publication_date_non_numeric(base_valid_row):
    base_valid_row["publication_date"] = "2018-01-15"

    record = map_row(base_valid_row)

    assert record is not None
    assert record.date == ""


def test_map_row_handles_missing_publication_date(base_valid_row):
    base_valid_row.pop("publication_date")

    record = map_row(base_valid_row)

    assert record is not None
    assert record.date == ""


def test_map_row_handles_missing_assignee(base_valid_row):
    base_valid_row.pop("assignee")

    record = map_row(base_valid_row)

    assert record is not None
    assert record.applicant == ""


def test_map_row_handles_empty_assignee(base_valid_row):
    base_valid_row["assignee"] = ""

    record = map_row(base_valid_row)

    assert record is not None
    assert record.applicant == ""


def test_map_row_handles_multiple_cpc_codes_as_joined_string(base_valid_row):
    base_valid_row["cpc_codes"] = "G06F 16/93; G06N 20/00; H04L 29/08"

    record = map_row(base_valid_row)

    assert record is not None
    assert record.metadata["cpc_codes"] == "G06F 16/93; G06N 20/00; H04L 29/08"
    assert isinstance(record.metadata["cpc_codes"], str)


def test_map_row_normalizes_url_with_slashes_and_spaces(base_valid_row):
    base_valid_row["publication_number"] = "US 10/123456 B2"

    record = map_row(base_valid_row)

    assert record is not None
    assert record.url == "https://patents.google.com/patent/US10123456B2"


def test_map_row_omits_empty_metadata_fields(base_valid_row):
    base_valid_row["country_code"] = ""
    base_valid_row["kind_code"] = None
    base_valid_row.pop("application_number")
    base_valid_row["family_id"] = "   "
    base_valid_row["filing_date"] = "invalid"

    record = map_row(base_valid_row)

    assert record is not None
    assert "country_code" not in record.metadata
    assert "kind_code" not in record.metadata
    assert "application_number" not in record.metadata
    assert "family_id" not in record.metadata
    assert "filing_date" not in record.metadata
    assert record.metadata["cpc_codes"] == "G06F 16/93; G06N 20/00"


def test_map_row_normalizes_filing_date(base_valid_row):
    base_valid_row["filing_date"] = 20161220

    record = map_row(base_valid_row)

    assert record is not None
    assert record.metadata["filing_date"] == "2016-12-20"


def test_map_rows_returns_valid_records_and_skipped_count():
    rows = [
        {
            "publication_number": "US1000",
            "title": "Patent A",
            "abstract": "Abstract A",
            "assignee": "Company A",
            "publication_date": "20180101",
            "cpc_codes": "A01B 1/00",
        },
        {
            "publication_number": "",
            "title": "Patent B",
            "abstract": "Abstract B",
        },
        {
            "publication_number": "US1001",
            "title": "Patent C",
            "abstract": "Abstract C",
            "assignee": "Company C",
            "publication_date": "20190201",
            "cpc_codes": "B01C 2/00",
        },
        {
            "publication_number": "US1002",
            "title": "",
            "abstract": "",
        },
        {
            "publication_number": "US1003",
            "title": "Patent D",
            "abstract": "",
            "assignee": "Company D",
            "publication_date": "20200301",
            "cpc_codes": "C01D 3/00",
        },
    ]

    records, skipped_count = map_rows(rows)

    assert len(records) == 3
    assert skipped_count == 2
    assert records[0].reference == "US1000"
    assert records[0].title == "Patent A"
    assert records[1].reference == "US1001"
    assert records[1].title == "Patent C"
    assert records[2].reference == "US1003"
    assert records[2].title == "Patent D"


def test_map_rows_returns_empty_list_when_all_rows_skipped():
    rows = [
        {"publication_number": "", "title": "Title", "abstract": "Abstract"},
        {"publication_number": "US1000", "title": "", "abstract": ""},
        {"title": "Title", "abstract": "Abstract"},
    ]

    records, skipped_count = map_rows(rows)

    assert records == []
    assert skipped_count == 3


def test_map_rows_returns_all_records_when_none_skipped():
    rows = [
        {
            "publication_number": "US1000",
            "title": "Patent A",
            "abstract": "Abstract A",
        },
        {
            "publication_number": "US1001",
            "title": "Patent B",
            "abstract": "Abstract B",
        },
    ]

    records, skipped_count = map_rows(rows)

    assert len(records) == 2
    assert skipped_count == 0


def test_map_rows_preserves_order():
    rows = [
        {"publication_number": "US1003", "title": "C", "abstract": "C"},
        {"publication_number": "US1001", "title": "A", "abstract": "A"},
        {"publication_number": "US1002", "title": "B", "abstract": "B"},
    ]

    records, skipped_count = map_rows(rows)

    assert len(records) == 3
    assert records[0].reference == "US1003"
    assert records[1].reference == "US1001"
    assert records[2].reference == "US1002"


def test_map_rows_with_empty_list():
    records, skipped_count = map_rows([])

    assert records == []
    assert skipped_count == 0
