import logging
import re

from evidence.domain.errors.evidence_errors import CorpusLoadError
from evidence.domain.models.patent_corpus_record import PatentCorpusRecord
from evidence.infrastructure.config.bigquery_settings import BigQueryCorpusSettings
from evidence.infrastructure.corpus.bigquery.bigquery_client import (
    BigQueryAuthError,
    BigQueryClient,
    BigQueryPermissionError,
    BigQueryQueryError,
    BigQueryTransportError,
)
from evidence.infrastructure.corpus.bigquery.bigquery_row_mapper import map_rows

_BYTES_PER_GIB = 1024 * 1024 * 1024
_COUNTRY_CODE_PATTERN = re.compile(r"^[A-Z]{2}$")
_CPC_PREFIX_PATTERN = re.compile(r"^[A-Z0-9]*$")
_QUERY_TEMPLATE = (
    "SELECT\n"
    "  p.publication_number                                   AS publication_number,\n"
    "  p.country_code                                         AS country_code,\n"
    "  p.kind_code                                            AS kind_code,\n"
    "  p.application_number                                   AS application_number,\n"
    "  CAST(p.family_id AS STRING)                            AS family_id,\n"
    "  p.publication_date                                     AS publication_date,\n"
    "  p.filing_date                                          AS filing_date,\n"
    "  (SELECT t.text FROM UNNEST(p.title_localized) AS t "
    "WHERE t.language = 'en' LIMIT 1) AS title,\n"
    "  (SELECT a.text FROM UNNEST(p.abstract_localized) AS a "
    "WHERE a.language = 'en' LIMIT 1) AS abstract,\n"
    "  (SELECT ah.name FROM UNNEST(p.assignee_harmonized) AS ah LIMIT 1) "
    "AS assignee,\n"
    "  (SELECT STRING_AGG(DISTINCT c.code, ';' ORDER BY c.code) "
    "FROM UNNEST(p.cpc) AS c) AS cpc_codes\n"
    "FROM `patents-public-data.patents.publications` AS p\n"
    "WHERE p.country_code = '{country}'\n"
    "  AND ({pub_date_start} = 0 OR p.publication_date >= {pub_date_start})\n"
    "  AND ({pub_date_end}   = 0 OR p.publication_date <= {pub_date_end})\n"
    "  AND EXISTS (SELECT 1 FROM UNNEST(p.title_localized) AS t "
    "WHERE t.language = 'en' AND t.text != '')\n"
    "  AND EXISTS (SELECT 1 FROM UNNEST(p.abstract_localized) AS a "
    "WHERE a.language = 'en' AND a.text != '')\n"
    "  AND ('{cpc_prefix}' = '' OR EXISTS (SELECT 1 FROM UNNEST(p.cpc) AS c "
    "WHERE STARTS_WITH(c.code, '{cpc_prefix}')))\n"
    "LIMIT {row_limit};\n"
)

_logger = logging.getLogger(__name__)


def _validate_country_code(country: str) -> str:
    if not _COUNTRY_CODE_PATTERN.match(country):
        raise CorpusLoadError(f"Invalid country code: {country}")
    return country


def _validate_cpc_prefix(cpc_prefix: str) -> str:
    if not _CPC_PREFIX_PATTERN.match(cpc_prefix):
        raise CorpusLoadError(f"Invalid CPC prefix: {cpc_prefix}")
    return cpc_prefix


def _build_sql(settings: BigQueryCorpusSettings) -> str:
    validated_country = _validate_country_code(settings.bigquery_country)
    validated_cpc = _validate_cpc_prefix(settings.bigquery_cpc_prefix)
    return _QUERY_TEMPLATE.format(
        country=validated_country,
        pub_date_start=settings.bigquery_date_from,
        pub_date_end=settings.bigquery_date_to,
        cpc_prefix=validated_cpc,
        row_limit=settings.bigquery_row_limit,
    )


def _log_dry_run_estimate(response: dict) -> None:
    bytes_processed = int(response.get("totalBytesProcessed", 0))
    gib = bytes_processed / _BYTES_PER_GIB
    _logger.info("BigQuery corpus query estimated cost: %d bytes (%.3f GiB)", bytes_processed, gib)


def _enforce_bytes_cap(response: dict, max_bytes_billed: int) -> None:
    estimated = int(response.get("totalBytesProcessed", 0))
    if estimated > max_bytes_billed:
        raise CorpusLoadError(
            f"BigQuery query would scan {estimated} bytes, exceeding the "
            f"{max_bytes_billed}-byte cap. Tighten the country/date filters or "
            "raise bigquery_max_bytes_billed.",
            loaded_count=0,
        )


def _handle_bigquery_error(error: Exception) -> None:
    if isinstance(error, BigQueryAuthError):
        raise CorpusLoadError(
            "BigQuery authentication failed. Check service account key.", loaded_count=0
        ) from error
    if isinstance(error, BigQueryPermissionError):
        raise CorpusLoadError(
            "BigQuery permission denied. Check dataset access rights.", loaded_count=0
        ) from error
    if isinstance(error, BigQueryQueryError):
        raise CorpusLoadError(
            f"BigQuery query error: {error}", status_code=error.status_code, loaded_count=0
        ) from error
    if isinstance(error, BigQueryTransportError):
        raise CorpusLoadError(f"BigQuery transport error: {error}", loaded_count=0) from error
    raise error


class BigQueryCorpusReader:
    def __init__(self, client: BigQueryClient, settings: BigQueryCorpusSettings) -> None:
        self._client = client
        self._settings = settings

    def read(self, file_path: str | None = None) -> list[PatentCorpusRecord]:
        sql = _build_sql(self._settings)
        max_bytes = self._settings.bigquery_max_bytes_billed
        try:
            dry_run_response = self._client.run_query(sql, dry_run=True)
            _log_dry_run_estimate(dry_run_response)
        except Exception as error:
            _handle_bigquery_error(error)
        _enforce_bytes_cap(dry_run_response, max_bytes)
        try:
            query_response = self._client.run_query(
                sql,
                dry_run=False,
                max_results=self._settings.bigquery_row_limit,
                max_bytes_billed=max_bytes,
            )
        except Exception as error:
            _handle_bigquery_error(error)
        rows = query_response.get("rows", [])
        records, skipped = map_rows(rows)
        _logger.info(
            "BigQuery corpus load complete: %d rows fetched, %d records mapped, %d skipped",
            len(rows),
            len(records),
            skipped,
        )
        return records
