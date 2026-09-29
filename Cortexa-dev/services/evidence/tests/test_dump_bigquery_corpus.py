import os
from pathlib import Path
from unittest.mock import Mock, patch

import pytest

from evidence.domain.errors.evidence_errors import CorpusLoadError
from evidence.domain.models.patent_corpus_record import PatentCorpusRecord
from evidence.scripts.dump_bigquery_corpus import (
    _build_client_and_reader,
    _get_output_path,
    _validate_bigquery_config,
    _write_jsonl,
)


@pytest.fixture
def valid_records():
    return [
        PatentCorpusRecord(
            reference="US10123456B2",
            title="Test patent one",
            abstract="First abstract",
            applicant="Tech Corp",
            date="2020-01-15",
            url="https://patents.google.com/patent/US10123456B2",
            metadata={"country_code": "US", "kind_code": "B2"},
        ),
        PatentCorpusRecord(
            reference="EP3456789A1",
            title="Test patent two",
            abstract="Second abstract",
            applicant="AI Innovations Ltd",
            date="2021-03-01",
            url="https://patents.google.com/patent/EP3456789A1",
            metadata={"country_code": "EP", "kind_code": "A1"},
        ),
    ]


@pytest.fixture
def bigquery_settings():
    with patch.dict(
        os.environ,
        {
            "CORPUS_SOURCE": "bigquery",
            "GCP_PROJECT_ID": "test-project",
            "GCP_SERVICE_ACCOUNT_PATH": "/fake/path.json",
            "VECTOR_ROUTER_URL": "http://test-vector-router",
        },
    ):
        from evidence.infrastructure.config.bigquery_settings import BigQueryCorpusSettings

        return BigQueryCorpusSettings()


def test_get_output_path_default():
    with patch.dict(os.environ, {}, clear=False):
        if "CORPUS_DUMP_PATH" in os.environ:
            del os.environ["CORPUS_DUMP_PATH"]
        path = _get_output_path()
        assert path == Path("data/corpus/bigquery_dump.jsonl")


def test_get_output_path_from_env():
    with patch.dict(os.environ, {"CORPUS_DUMP_PATH": "custom/output.jsonl"}):
        path = _get_output_path()
        assert path == Path("custom/output.jsonl")


def test_validate_bigquery_config_happy_path(bigquery_settings):
    _validate_bigquery_config(bigquery_settings)


def test_validate_bigquery_config_rejects_file_source():
    with patch.dict(os.environ, {"CORPUS_SOURCE": "file", "VECTOR_ROUTER_URL": "http://test"}):
        from evidence.infrastructure.config.bigquery_settings import BigQueryCorpusSettings

        settings = BigQueryCorpusSettings()
        with pytest.raises(CorpusLoadError) as exc_info:
            _validate_bigquery_config(settings)
        assert "CORPUS_SOURCE must be 'bigquery'" in str(exc_info.value)


def test_validate_bigquery_config_rejects_missing_project_id():
    with patch.dict(
        os.environ,
        {
            "CORPUS_SOURCE": "bigquery",
            "GCP_PROJECT_ID": "",
            "GCP_SERVICE_ACCOUNT_PATH": "/fake/path.json",
            "VECTOR_ROUTER_URL": "http://test",
        },
    ):
        from evidence.infrastructure.config.bigquery_settings import BigQueryCorpusSettings

        settings = BigQueryCorpusSettings()
        with pytest.raises(CorpusLoadError) as exc_info:
            _validate_bigquery_config(settings)
        assert "GCP_PROJECT_ID" in str(exc_info.value)


def test_validate_bigquery_config_rejects_missing_sa_path():
    with patch.dict(
        os.environ,
        {
            "CORPUS_SOURCE": "bigquery",
            "GCP_PROJECT_ID": "test-project",
            "GCP_SERVICE_ACCOUNT_PATH": "",
            "VECTOR_ROUTER_URL": "http://test",
        },
    ):
        from evidence.infrastructure.config.bigquery_settings import BigQueryCorpusSettings

        settings = BigQueryCorpusSettings()
        with pytest.raises(CorpusLoadError) as exc_info:
            _validate_bigquery_config(settings)
        assert "GCP_SERVICE_ACCOUNT_PATH" in str(exc_info.value)


def test_write_jsonl_creates_parent_dirs(tmp_path, valid_records):
    output_path = tmp_path / "nested" / "dir" / "output.jsonl"
    _write_jsonl(valid_records, output_path)

    assert output_path.exists()
    lines = output_path.read_text().splitlines()
    assert len(lines) == 2


def test_write_jsonl_valid_json_lines(tmp_path, valid_records):
    output_path = tmp_path / "output.jsonl"
    _write_jsonl(valid_records, output_path)

    lines = output_path.read_text().splitlines()
    assert len(lines) == 2

    import json

    record1 = json.loads(lines[0])
    assert record1["reference"] == "US10123456B2"
    assert record1["title"] == "Test patent one"
    assert record1["abstract"] == "First abstract"
    assert record1["applicant"] == "Tech Corp"
    assert record1["date"] == "2020-01-15"
    assert record1["url"] == "https://patents.google.com/patent/US10123456B2"
    assert record1["metadata"]["country_code"] == "US"

    record2 = json.loads(lines[1])
    assert record2["reference"] == "EP3456789A1"
    assert record2["title"] == "Test patent two"


def test_write_jsonl_empty_list(tmp_path):
    output_path = tmp_path / "empty.jsonl"
    _write_jsonl([], output_path)

    assert output_path.exists()
    content = output_path.read_text()
    assert content == ""


@patch("evidence.scripts.dump_bigquery_corpus.BigQueryClient")
@patch("evidence.scripts.dump_bigquery_corpus.BigQueryCorpusReader")
def test_build_client_and_reader_constructs_both(
    mock_reader_cls, mock_client_cls, bigquery_settings
):
    mock_client = Mock()
    mock_client_cls.return_value = mock_client
    mock_reader = Mock()
    mock_reader_cls.return_value = mock_reader

    reader = _build_client_and_reader(bigquery_settings)

    mock_client_cls.assert_called_once_with("test-project", "/fake/path.json")
    mock_reader_cls.assert_called_once_with(mock_client, bigquery_settings)
    assert reader is mock_reader
