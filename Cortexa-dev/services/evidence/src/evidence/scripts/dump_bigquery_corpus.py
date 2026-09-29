import logging
import os
import sys
from pathlib import Path

from evidence.domain.errors.evidence_errors import CorpusLoadError
from evidence.domain.models.patent_corpus_record import PatentCorpusRecord
from evidence.infrastructure.config.bigquery_settings import BigQueryCorpusSettings
from evidence.infrastructure.corpus.bigquery.bigquery_client import (
    BigQueryAuthError,
    BigQueryClient,
    BigQueryPermissionError,
)
from evidence.infrastructure.corpus.bigquery.bigquery_corpus_reader import (
    BigQueryCorpusReader,
)

BIGQUERY_SOURCE = "bigquery"
DEFAULT_OUTPUT_PATH = "data/corpus/bigquery_dump.jsonl"

_logger = logging.getLogger(__name__)


def _configure_logging() -> None:
    logging.basicConfig(
        level=logging.INFO,
        format="%(asctime)s - %(name)s - %(levelname)s - %(message)s",
    )


def _get_output_path() -> Path:
    raw = os.getenv("CORPUS_DUMP_PATH", DEFAULT_OUTPUT_PATH)
    return Path(raw)


def _validate_bigquery_config(settings: BigQueryCorpusSettings) -> None:
    if settings.corpus_source != BIGQUERY_SOURCE:
        raise CorpusLoadError(
            f"CORPUS_SOURCE must be '{BIGQUERY_SOURCE}', got '{settings.corpus_source}'"
        )
    if not settings.gcp_project_id.strip():
        raise CorpusLoadError(
            "GCP_PROJECT_ID environment variable is required for BigQuery corpus dump"
        )
    if not settings.gcp_service_account_path.strip():
        raise CorpusLoadError(
            "GCP_SERVICE_ACCOUNT_PATH environment variable is required for BigQuery corpus dump"
        )


def _build_client_and_reader(settings: BigQueryCorpusSettings):
    client = BigQueryClient(settings.gcp_project_id, settings.gcp_service_account_path)
    return BigQueryCorpusReader(client, settings)


def _write_jsonl(records: list[PatentCorpusRecord], output_path: Path) -> None:
    output_path.parent.mkdir(parents=True, exist_ok=True)
    with output_path.open("w") as f:
        for record in records:
            f.write(record.model_dump_json())
            f.write("\n")


def _log_summary(count: int, output_path: Path) -> None:
    _logger.info("BigQuery corpus dump complete: %d records written to %s", count, output_path)


_ERROR_MESSAGES: tuple[tuple[type[Exception], str], ...] = (
    (BigQueryAuthError, "BigQuery authentication failed"),
    (BigQueryPermissionError, "BigQuery permission denied"),
    (CorpusLoadError, "Corpus dump error"),
)


def _log_error(error: Exception) -> None:
    for error_type, message in _ERROR_MESSAGES:
        if isinstance(error, error_type):
            _logger.error("%s: %s", message, error)
            return
    _logger.exception("Unexpected error: %s", error)


def main() -> None:
    _configure_logging()
    failed = False
    try:
        settings = BigQueryCorpusSettings()
        _validate_bigquery_config(settings)
        reader = _build_client_and_reader(settings)
        records = reader.read()
        output_path = _get_output_path()
        _write_jsonl(records, output_path)
        _log_summary(len(records), output_path)
    except Exception as error:
        _log_error(error)
        failed = True
    if failed:
        sys.exit(1)


if __name__ == "__main__":
    main()
