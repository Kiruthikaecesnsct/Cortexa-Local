import asyncio
import logging
import sys

from evidence.application.handlers.load_corpus_handler import (
    LoadCorpusDeps,
    LoadCorpusHandler,
)
from evidence.domain.errors.evidence_errors import CorpusLoadError
from evidence.infrastructure.config.bigquery_settings import BigQueryCorpusSettings
from evidence.infrastructure.config.settings import EvidenceSettings
from evidence.infrastructure.corpus.bigquery.bigquery_client import (
    BigQueryAuthError,
    BigQueryClient,
    BigQueryPermissionError,
)
from evidence.infrastructure.corpus.bigquery.bigquery_corpus_reader import (
    BigQueryCorpusReader,
)
from evidence.infrastructure.corpus.corpus_file_reader import CorpusFileReader
from evidence.infrastructure.corpus.corpus_loader_adapter import CorpusLoaderAdapter

BIGQUERY_SOURCE = "bigquery"
FILE_SOURCE = "file"

_logger = logging.getLogger(__name__)


def _configure_logging() -> None:
    logging.basicConfig(
        level=logging.INFO,
        format="%(asctime)s - %(name)s - %(levelname)s - %(message)s",
    )


def _build_reader(bq_settings: BigQueryCorpusSettings, ev_settings: EvidenceSettings):
    if bq_settings.corpus_source == BIGQUERY_SOURCE:
        _validate_bigquery_config(bq_settings)
        client = BigQueryClient(bq_settings.gcp_project_id, bq_settings.gcp_service_account_path)
        return BigQueryCorpusReader(client, bq_settings)
    return CorpusFileReader(ev_settings.corpus_file_path)


def _validate_bigquery_config(settings: BigQueryCorpusSettings) -> None:
    if not settings.gcp_project_id.strip():
        raise CorpusLoadError(
            "GCP_PROJECT_ID environment variable is required for corpus_source=bigquery"
        )
    if not settings.gcp_service_account_path.strip():
        raise CorpusLoadError(
            "GCP_SERVICE_ACCOUNT_PATH environment variable is required for corpus_source=bigquery"
        )


async def _run_load() -> int:
    bq_settings = BigQueryCorpusSettings()
    ev_settings = EvidenceSettings()
    reader = _build_reader(bq_settings, ev_settings)
    loader = CorpusLoaderAdapter(ev_settings)
    handler = LoadCorpusHandler(LoadCorpusDeps(loader, reader, ev_settings))
    try:
        result = await handler.bulk_load()
        return result.loaded_count
    finally:
        await loader.aclose()


def _log_summary(loaded: int, failed: bool) -> None:
    if failed:
        _logger.error("Corpus load failed")
    else:
        _logger.info("Corpus load complete: loaded=%d", loaded)


# Actionable, credential-free message per failure class (order = most specific first).
_ERROR_MESSAGES: tuple[tuple[type[Exception], str], ...] = (
    (BigQueryAuthError, "BigQuery authentication failed"),
    (BigQueryPermissionError, "BigQuery permission denied"),
    (CorpusLoadError, "Corpus load error"),
)


def _log_error(error: Exception) -> None:
    for error_type, message in _ERROR_MESSAGES:
        if isinstance(error, error_type):
            _logger.error("%s: %s", message, error)
            return
    _logger.exception("Unexpected error: %s", error)


def main() -> None:
    _configure_logging()
    loaded = 0
    failed = False
    try:
        loaded = asyncio.run(_run_load())
    except Exception as error:  # noqa: BLE001 — dispatched to a credential-free logger
        _log_error(error)
        failed = True
    finally:
        _log_summary(loaded, failed)
    if failed:
        sys.exit(1)


if __name__ == "__main__":
    main()
