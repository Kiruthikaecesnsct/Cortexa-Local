from evidence.domain.models.patent_corpus_record import PatentCorpusRecord
from evidence.infrastructure.corpus.record_mapper import build_payload, to_vector_item


def _make_record(**overrides) -> PatentCorpusRecord:
    defaults = {
        "reference": "US11234567",
        "title": "Sparse tensor method",
        "abstract": "A method for sparse tensor compression.",
        "applicant": "Acme Corp",
        "date": "2024-01-10",
        "url": "https://patents.example.com/US11234567",
        "metadata": {"jurisdiction": "US", "claim_count": 18},
    }
    defaults.update(overrides)
    return PatentCorpusRecord(**defaults)


def test_build_payload_contains_required_read_side_keys():
    record = _make_record()

    payload = build_payload(record)

    assert payload["title"] == "Sparse tensor method"
    assert payload["applicant"] == "Acme Corp"
    assert payload["date"] == "2024-01-10"
    assert payload["url"] == "https://patents.example.com/US11234567"


def test_build_payload_flattens_metadata_with_prefix():
    record = _make_record()

    payload = build_payload(record)

    assert payload["meta_jurisdiction"] == "US"
    assert payload["meta_claim_count"] == 18
    assert "metadata" not in payload


def test_build_payload_values_are_flat_scalars():
    record = _make_record()

    payload = build_payload(record)

    for value in payload.values():
        assert isinstance(value, str | int | float | bool)


def test_to_vector_item_uses_reference_as_id():
    record = _make_record()
    vector = [0.1, 0.2, 0.3]

    item = to_vector_item(record, vector)

    assert item["id"] == "US11234567"
    assert item["vector"] == vector
    assert item["payload"]["title"] == "Sparse tensor method"
