"""Gated, opt-in live integration test for US107 BigQuery corpus loading.

This test runs a bounded, real query against Google's public patents dataset via
a real service-account key. It is SKIPPED unless the operator explicitly opts in
by setting RUN_BIGQUERY_LIVE_TEST=1 AND supplying real GCP credentials, so CI and
the default test run never touch the network or need credentials.

Run it locally with the optional deps installed (`uv sync --extra bigquery`):

    RUN_BIGQUERY_LIVE_TEST=1 \
    GCP_PROJECT_ID=<project> \
    GCP_SERVICE_ACCOUNT_PATH=<path-to-key.json> \
    uv run pytest tests/integration/test_bigquery_live_load.py
"""

import os

import pytest

from evidence.infrastructure.config.bigquery_settings import BigQueryCorpusSettings

_OPT_IN_FLAG = "RUN_BIGQUERY_LIVE_TEST"
_PROJECT_ENV = "GCP_PROJECT_ID"
_KEY_PATH_ENV = "GCP_SERVICE_ACCOUNT_PATH"

# Small bound so the live query stays cheap and fast.
_LIVE_ROW_LIMIT = 5

pytestmark = pytest.mark.skipif(
    os.getenv(_OPT_IN_FLAG) != "1" or not os.getenv(_PROJECT_ENV) or not os.getenv(_KEY_PATH_ENV),
    reason=(
        f"live BigQuery test is opt-in: set {_OPT_IN_FLAG}=1, {_PROJECT_ENV}, "
        f"and {_KEY_PATH_ENV} (with the bigquery optional deps installed) to run it"
    ),
)


@pytest.fixture
def live_settings() -> BigQueryCorpusSettings:
    return BigQueryCorpusSettings(
        corpus_source="bigquery",
        gcp_project_id=os.environ[_PROJECT_ENV],
        gcp_service_account_path=os.environ[_KEY_PATH_ENV],
        bigquery_row_limit=_LIVE_ROW_LIMIT,
        bigquery_country="US",
        vector_router_url="http://unused.local",
    )


def test_live_bounded_query_returns_records(live_settings: BigQueryCorpusSettings) -> None:
    # Imported lazily so the module still collects without the bigquery extra.
    from evidence.infrastructure.corpus.bigquery.bigquery_client import BigQueryClient
    from evidence.infrastructure.corpus.bigquery.bigquery_corpus_reader import (
        BigQueryCorpusReader,
    )

    client = BigQueryClient(
        project_id=live_settings.gcp_project_id,
        service_account_path=live_settings.gcp_service_account_path,
    )
    reader = BigQueryCorpusReader(client, live_settings)

    records = reader.read()

    assert len(records) >= 1
    assert len(records) <= _LIVE_ROW_LIMIT
    first = records[0]
    assert first.reference
    assert first.embedding_text().strip()
