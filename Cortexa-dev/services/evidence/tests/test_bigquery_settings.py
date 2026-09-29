import pytest
from pydantic import ValidationError

from evidence.infrastructure.config.bigquery_settings import BigQueryCorpusSettings

_VECTOR_ROUTER_URL = "http://test-vector-router"


def test_default_corpus_source_is_file():
    settings = BigQueryCorpusSettings(vector_router_url=_VECTOR_ROUTER_URL)
    assert settings.corpus_source == "file"


def test_corpus_source_bigquery_parses():
    settings = BigQueryCorpusSettings(
        corpus_source="bigquery",
        vector_router_url=_VECTOR_ROUTER_URL,
    )
    assert settings.corpus_source == "bigquery"


def test_unbounded_date_from_accepted():
    settings = BigQueryCorpusSettings(
        bigquery_date_from=0,
        vector_router_url=_VECTOR_ROUTER_URL,
    )
    assert settings.bigquery_date_from == 0


def test_unbounded_date_to_accepted():
    settings = BigQueryCorpusSettings(
        bigquery_date_to=0,
        vector_router_url=_VECTOR_ROUTER_URL,
    )
    assert settings.bigquery_date_to == 0


def test_valid_date_from_accepted():
    settings = BigQueryCorpusSettings(
        bigquery_date_from=20200101,
        vector_router_url=_VECTOR_ROUTER_URL,
    )
    assert settings.bigquery_date_from == 20200101


def test_valid_date_to_accepted():
    settings = BigQueryCorpusSettings(
        bigquery_date_to=20251231,
        vector_router_url=_VECTOR_ROUTER_URL,
    )
    assert settings.bigquery_date_to == 20251231


def test_invalid_date_from_rejected():
    with pytest.raises(ValidationError) as exc_info:
        BigQueryCorpusSettings(
            bigquery_date_from=123,
            vector_router_url=_VECTOR_ROUTER_URL,
        )
    assert "bigquery_date_from" in str(exc_info.value)


def test_invalid_date_to_rejected():
    with pytest.raises(ValidationError) as exc_info:
        BigQueryCorpusSettings(
            bigquery_date_to=999999999,
            vector_router_url=_VECTOR_ROUTER_URL,
        )
    assert "bigquery_date_to" in str(exc_info.value)


def test_date_below_lower_bound_rejected():
    with pytest.raises(ValidationError) as exc_info:
        BigQueryCorpusSettings(
            bigquery_date_from=18991231,
            vector_router_url=_VECTOR_ROUTER_URL,
        )
    assert "19000101" in str(exc_info.value)


def test_date_above_upper_bound_rejected():
    with pytest.raises(ValidationError) as exc_info:
        BigQueryCorpusSettings(
            bigquery_date_to=100000101,
            vector_router_url=_VECTOR_ROUTER_URL,
        )
    assert "99991231" in str(exc_info.value)


def test_blank_vector_router_url_rejected():
    with pytest.raises(ValidationError) as exc_info:
        BigQueryCorpusSettings(vector_router_url="")
    assert "vector_router_url" in str(exc_info.value)


def test_whitespace_only_vector_router_url_rejected():
    with pytest.raises(ValidationError) as exc_info:
        BigQueryCorpusSettings(vector_router_url="   ")
    assert "vector_router_url" in str(exc_info.value)


def test_all_bigquery_fields_via_constructor():
    settings = BigQueryCorpusSettings(
        corpus_source="bigquery",
        gcp_project_id="test-project",
        gcp_service_account_path="/fake/path.json",
        bigquery_row_limit=5000,
        bigquery_country="DE",
        bigquery_date_from=20100101,
        bigquery_date_to=20201231,
        bigquery_cpc_prefix="H04L",
        vector_router_url=_VECTOR_ROUTER_URL,
    )
    assert settings.corpus_source == "bigquery"
    assert settings.gcp_project_id == "test-project"
    assert settings.gcp_service_account_path == "/fake/path.json"
    assert settings.bigquery_row_limit == 5000
    assert settings.bigquery_country == "DE"
    assert settings.bigquery_date_from == 20100101
    assert settings.bigquery_date_to == 20201231
    assert settings.bigquery_cpc_prefix == "H04L"
    assert settings.vector_router_url == _VECTOR_ROUTER_URL
