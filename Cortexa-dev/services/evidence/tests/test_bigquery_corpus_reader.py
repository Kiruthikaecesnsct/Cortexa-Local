from unittest.mock import Mock

import pytest

from evidence.domain.errors.evidence_errors import CorpusLoadError
from evidence.infrastructure.config.bigquery_settings import BigQueryCorpusSettings
from evidence.infrastructure.corpus.bigquery.bigquery_client import (
    BigQueryAuthError,
    BigQueryPermissionError,
)
from evidence.infrastructure.corpus.bigquery.bigquery_corpus_reader import (
    BigQueryCorpusReader,
)


@pytest.fixture
def settings():
    return BigQueryCorpusSettings(
        corpus_source="bigquery",
        gcp_project_id="test-project",
        gcp_service_account_path="/fake/path.json",
        bigquery_row_limit=10,
        bigquery_country="US",
        bigquery_date_from=0,
        bigquery_date_to=0,
        bigquery_cpc_prefix="",
        vector_router_url="http://test-vector-router",
    )


@pytest.fixture
def mock_client():
    return Mock()


@pytest.fixture
def valid_rows():
    return [
        {
            "publication_number": "US10123456B2",
            "country_code": "US",
            "kind_code": "B2",
            "application_number": "15123456",
            "family_id": "60123456",
            "publication_date": "20200115",
            "filing_date": "20180315",
            "title": "System and method for sparse tensor processing",
            "abstract": "A novel approach to processing sparse tensors efficiently.",
            "assignee": "Tech Corp",
            "cpc_codes": "G06F17/16;G06N3/08",
        },
        {
            "publication_number": "EP3456789A1",
            "country_code": "EP",
            "kind_code": "A1",
            "application_number": "18123456",
            "family_id": "62345678",
            "publication_date": "20210301",
            "filing_date": "20190801",
            "title": "Machine learning optimization technique",
            "abstract": "Improved optimization for neural network training.",
            "assignee": "AI Innovations Ltd",
            "cpc_codes": "G06N3/04;G06N20/00",
        },
    ]


def test_happy_path_runs_dry_then_real_query(settings, mock_client, valid_rows):
    mock_client.run_query.side_effect = [
        {"totalBytesProcessed": "123456"},
        {"rows": valid_rows},
    ]
    reader = BigQueryCorpusReader(mock_client, settings)

    records = reader.read()

    assert mock_client.run_query.call_count == 2
    first_call = mock_client.run_query.call_args_list[0]
    assert first_call.kwargs["dry_run"] is True
    second_call = mock_client.run_query.call_args_list[1]
    assert second_call.kwargs["dry_run"] is False
    assert second_call.kwargs["max_results"] == 10
    assert len(records) == 2
    assert records[0].reference == "US10123456B2"
    assert records[0].title == "System and method for sparse tensor processing"
    assert records[1].reference == "EP3456789A1"


def test_happy_path_passes_bytes_cap_to_real_query(settings, mock_client, valid_rows):
    settings.bigquery_max_bytes_billed = 1_000_000
    mock_client.run_query.side_effect = [
        {"totalBytesProcessed": "500000"},
        {"rows": valid_rows},
    ]
    reader = BigQueryCorpusReader(mock_client, settings)

    reader.read()

    second_call = mock_client.run_query.call_args_list[1]
    assert second_call.kwargs["max_bytes_billed"] == 1_000_000


def test_estimate_over_cap_raises_before_real_query(settings, mock_client):
    settings.bigquery_max_bytes_billed = 1_000_000
    # Only the dry run should run; the estimate exceeds the cap.
    mock_client.run_query.side_effect = [{"totalBytesProcessed": "2000000"}]
    reader = BigQueryCorpusReader(mock_client, settings)

    with pytest.raises(CorpusLoadError) as exc_info:
        reader.read()

    assert "cap" in str(exc_info.value).lower()
    assert mock_client.run_query.call_count == 1  # real query never ran


def test_bytes_logged_from_dry_run(settings, mock_client, valid_rows, caplog):
    mock_client.run_query.side_effect = [
        {"totalBytesProcessed": "987654321"},
        {"rows": valid_rows},
    ]
    reader = BigQueryCorpusReader(mock_client, settings)

    with caplog.at_level("INFO"):
        reader.read()

    log_text = " ".join(rec.message for rec in caplog.records)
    assert "bytes" in log_text.lower()
    assert "987654321" in log_text


def test_bad_country_raises_corpus_load_error_before_query(settings, mock_client):
    settings.bigquery_country = "USA"
    reader = BigQueryCorpusReader(mock_client, settings)

    with pytest.raises(CorpusLoadError) as exc_info:
        reader.read()

    assert "Invalid country code" in str(exc_info.value)
    mock_client.run_query.assert_not_called()


def test_bad_cpc_prefix_raises_corpus_load_error_before_query(settings, mock_client):
    settings.bigquery_cpc_prefix = "H04L!"
    reader = BigQueryCorpusReader(mock_client, settings)

    with pytest.raises(CorpusLoadError) as exc_info:
        reader.read()

    assert "Invalid CPC prefix" in str(exc_info.value)
    mock_client.run_query.assert_not_called()


def test_bigquery_permission_error_mapped(settings, mock_client):
    mock_client.run_query.side_effect = BigQueryPermissionError("Access denied")
    reader = BigQueryCorpusReader(mock_client, settings)

    with pytest.raises(CorpusLoadError) as exc_info:
        reader.read()

    assert "permission denied" in str(exc_info.value).lower()


def test_bigquery_auth_error_mapped(settings, mock_client):
    mock_client.run_query.side_effect = BigQueryAuthError("Invalid credentials")
    reader = BigQueryCorpusReader(mock_client, settings)

    with pytest.raises(CorpusLoadError) as exc_info:
        reader.read()

    assert "authentication failed" in str(exc_info.value).lower()


def test_empty_result_returns_empty_list(settings, mock_client):
    mock_client.run_query.side_effect = [
        {"totalBytesProcessed": "0"},
        {"rows": []},
    ]
    reader = BigQueryCorpusReader(mock_client, settings)

    records = reader.read()

    assert records == []
    assert mock_client.run_query.call_count == 2


def test_skip_counting_excludes_malformed_rows(settings, mock_client):
    rows_with_skip = [
        {
            "publication_number": "",
            "title": "Missing publication number",
            "abstract": "Should be skipped",
        },
        {
            "publication_number": "US10999999B2",
            "country_code": "US",
            "kind_code": "B2",
            "application_number": "15999999",
            "family_id": "60999999",
            "publication_date": "20220101",
            "filing_date": "20200101",
            "title": "Valid patent title",
            "abstract": "Valid abstract",
            "assignee": "Valid Corp",
            "cpc_codes": "H04L29/06",
        },
    ]
    mock_client.run_query.side_effect = [
        {"totalBytesProcessed": "100"},
        {"rows": rows_with_skip},
    ]
    reader = BigQueryCorpusReader(mock_client, settings)

    records = reader.read()

    assert len(records) == 1
    assert records[0].reference == "US10999999B2"


def test_rows_with_title_but_no_abstract_accepted(settings, mock_client):
    rows = [
        {
            "publication_number": "US10111111B1",
            "title": "Title only",
            "abstract": "",
        },
    ]
    mock_client.run_query.side_effect = [
        {"totalBytesProcessed": "50"},
        {"rows": rows},
    ]
    reader = BigQueryCorpusReader(mock_client, settings)

    records = reader.read()

    assert len(records) == 1
    assert records[0].reference == "US10111111B1"
    assert records[0].title == "Title only"
    assert records[0].abstract == ""


def test_rows_with_abstract_but_no_title_accepted(settings, mock_client):
    rows = [
        {
            "publication_number": "US10333333B1",
            "title": "",
            "abstract": "Abstract only",
        },
    ]
    mock_client.run_query.side_effect = [
        {"totalBytesProcessed": "50"},
        {"rows": rows},
    ]
    reader = BigQueryCorpusReader(mock_client, settings)

    records = reader.read()

    assert len(records) == 1
    assert records[0].reference == "US10333333B1"
    assert records[0].title == ""
    assert records[0].abstract == "Abstract only"


def test_rows_with_neither_title_nor_abstract_skipped(settings, mock_client):
    rows = [
        {
            "publication_number": "US10555555B1",
            "title": "",
            "abstract": "",
        },
        {
            "publication_number": "US10666666B1",
            "title": "Valid title",
            "abstract": "Valid abstract",
        },
    ]
    mock_client.run_query.side_effect = [
        {"totalBytesProcessed": "50"},
        {"rows": rows},
    ]
    reader = BigQueryCorpusReader(mock_client, settings)

    records = reader.read()

    assert len(records) == 1
    assert records[0].reference == "US10666666B1"
