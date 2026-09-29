import pytest

from evidence.domain.errors.evidence_errors import CorpusLoadError
from evidence.infrastructure.corpus.corpus_file_reader import CorpusFileReader

VALID_LINE = (
    '{"reference": "US1000", "title": "Patent A", "abstract": "An abstract.", '
    '"applicant": "Acme Corp", "date": "2024-01-01", '
    '"url": "https://patents.example.com/US1000", "metadata": {"jurisdiction": "US"}}'
)


def test_read_parses_valid_jsonl(tmp_path):
    file_path = tmp_path / "seed.jsonl"
    file_path.write_text(f"{VALID_LINE}\n{VALID_LINE}\n", encoding="utf-8")
    reader = CorpusFileReader(str(file_path))

    records = reader.read()

    assert len(records) == 2
    assert records[0].reference == "US1000"
    assert records[0].title == "Patent A"


def test_read_skips_blank_lines(tmp_path):
    file_path = tmp_path / "seed.jsonl"
    file_path.write_text(f"{VALID_LINE}\n\n{VALID_LINE}\n", encoding="utf-8")
    reader = CorpusFileReader(str(file_path))

    records = reader.read()

    assert len(records) == 2


def test_read_raises_corpus_load_error_on_malformed_json(tmp_path):
    file_path = tmp_path / "seed.jsonl"
    file_path.write_text("not valid json\n", encoding="utf-8")
    reader = CorpusFileReader(str(file_path))

    with pytest.raises(CorpusLoadError):
        reader.read()


def test_read_raises_corpus_load_error_on_missing_field(tmp_path):
    file_path = tmp_path / "seed.jsonl"
    file_path.write_text('{"reference": "US1000"}\n', encoding="utf-8")
    reader = CorpusFileReader(str(file_path))

    with pytest.raises(CorpusLoadError):
        reader.read()


def test_read_raises_corpus_load_error_when_file_missing(tmp_path):
    reader = CorpusFileReader(str(tmp_path / "missing.jsonl"))

    with pytest.raises(CorpusLoadError):
        reader.read()


def test_read_uses_path_override(tmp_path):
    default_path = tmp_path / "default.jsonl"
    default_path.write_text(VALID_LINE + "\n", encoding="utf-8")
    override_path = tmp_path / "override.jsonl"
    override_path.write_text(f"{VALID_LINE}\n{VALID_LINE}\n", encoding="utf-8")
    reader = CorpusFileReader(str(default_path))

    records = reader.read(override_path.name)

    assert len(records) == 2


def test_read_rejects_path_traversal_override(tmp_path):
    default_path = tmp_path / "default.jsonl"
    default_path.write_text(VALID_LINE + "\n", encoding="utf-8")
    secret_path = tmp_path.parent / "secret.jsonl"
    secret_path.write_text(VALID_LINE + "\n", encoding="utf-8")
    reader = CorpusFileReader(str(default_path))

    with pytest.raises(CorpusLoadError):
        reader.read("../secret.jsonl")
