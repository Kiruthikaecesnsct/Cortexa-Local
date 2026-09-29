"""Maps flat BigQuery patent rows to PatentCorpusRecord.

The reader's SQL flattens the nested/repeated columns of
`patents-public-data.patents.publications` into scalar-ish fields before the
rows reach this mapper, so the mapper only reads scalars and joined strings.
It never trusts a single row: a malformed row is skipped, not fatal.
"""

from evidence.domain.models.patent_corpus_record import PatentCorpusRecord, ScalarValue
from evidence.domain.services.patent_url import canonical_google_patents_url

# publication_date / filing_date arrive as YYYYMMDD (e.g. 20180115).
_PATENT_DATE_LENGTH = 8

# Optional scalar columns copied verbatim into metadata when present.
_STRING_METADATA_COLUMNS = (
    "country_code",
    "kind_code",
    "application_number",
    "family_id",
    "cpc_codes",
)


def _clean_text(value: object) -> str:
    """Coerce a possibly-missing cell to a trimmed string."""
    if value is None:
        return ""
    return str(value).strip()


def _normalize_date(value: object) -> str:
    """Turn a YYYYMMDD int/str date into ISO `YYYY-MM-DD`, else ``""``."""
    raw = _clean_text(value)
    if len(raw) != _PATENT_DATE_LENGTH or not raw.isdigit():
        return ""
    return f"{raw[0:4]}-{raw[4:6]}-{raw[6:8]}"


def _add_string_metadata(metadata: dict[str, ScalarValue], row: dict) -> None:
    """Copy non-empty string columns into metadata."""
    for column in _STRING_METADATA_COLUMNS:
        cleaned = _clean_text(row.get(column))
        if cleaned:
            metadata[column] = cleaned


def _add_filing_date_metadata(metadata: dict[str, ScalarValue], row: dict) -> None:
    """Copy the normalized filing date into metadata when present."""
    filing_date = _normalize_date(row.get("filing_date"))
    if filing_date:
        metadata["filing_date"] = filing_date


def _build_metadata(row: dict) -> dict[str, ScalarValue]:
    """Collect scalar extras (country, kind, cpc codes, family, dates)."""
    metadata: dict[str, ScalarValue] = {}
    _add_string_metadata(metadata, row)
    _add_filing_date_metadata(metadata, row)
    return metadata


def map_row(row: dict) -> PatentCorpusRecord | None:
    """Map one flattened BigQuery row to a record, or None if unusable.

    Returns None when the row lacks a publication number or has neither a
    title nor an abstract (nothing to embed).
    """
    reference = _clean_text(row.get("publication_number"))
    if not reference:
        return None
    title = _clean_text(row.get("title"))
    abstract = _clean_text(row.get("abstract"))
    if not title and not abstract:
        return None
    return PatentCorpusRecord(
        reference=reference,
        title=title,
        abstract=abstract,
        applicant=_clean_text(row.get("assignee")),
        date=_normalize_date(row.get("publication_date")),
        url=canonical_google_patents_url(reference),
        metadata=_build_metadata(row),
    )


def map_rows(rows: list[dict]) -> tuple[list[PatentCorpusRecord], int]:
    """Map many rows, returning (records, skipped_count)."""
    records: list[PatentCorpusRecord] = []
    skipped = 0
    for row in rows:
        record = map_row(row)
        if record is None:
            skipped += 1
            continue
        records.append(record)
    return records, skipped
