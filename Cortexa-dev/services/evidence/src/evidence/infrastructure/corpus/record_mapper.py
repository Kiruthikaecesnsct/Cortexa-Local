from evidence.domain.models.patent_corpus_record import PatentCorpusRecord, ScalarValue

_METADATA_KEY_PREFIX = "meta_"


def _flatten_metadata(metadata: dict[str, ScalarValue]) -> dict[str, ScalarValue]:
    return {f"{_METADATA_KEY_PREFIX}{key}": value for key, value in metadata.items()}


def build_payload(record: PatentCorpusRecord) -> dict[str, ScalarValue]:
    payload: dict[str, ScalarValue] = {
        "title": record.title,
        "abstract": record.abstract,
        "applicant": record.applicant,
        "date": record.date,
        "url": record.url,
    }
    payload.update(_flatten_metadata(record.metadata))
    return payload


def to_vector_item(record: PatentCorpusRecord, vector: list[float]) -> dict:
    return {
        "id": record.reference,
        "vector": vector,
        "payload": build_payload(record),
    }
