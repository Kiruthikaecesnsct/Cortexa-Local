from typing import Literal

from pydantic import field_validator
from pydantic_settings import BaseSettings, SettingsConfigDict


class BigQueryCorpusSettings(BaseSettings):
    corpus_source: Literal["file", "bigquery"] = "file"
    gcp_project_id: str = ""
    gcp_service_account_path: str = ""
    bigquery_row_limit: int = 1000
    bigquery_country: str = "US"
    bigquery_date_from: int = 0
    bigquery_date_to: int = 0
    bigquery_cpc_prefix: str = ""
    # Hard ceiling on bytes a single query may scan. BigQuery aborts (no charge)
    # any query that would exceed this, and the reader also pre-checks the dry-run
    # estimate against it. A normal filtered pull (country + CPC + date) scans
    # ~230 GiB because BigQuery reads full columns regardless of LIMIT; the default
    # ~300 GiB leaves headroom above that while still catching a runaway scan.
    # Note 300 GiB is well under BigQuery's 1 TB/month always-free tier, so even a
    # query at the cap is $0. Lower it to tighten the guard.
    bigquery_max_bytes_billed: int = 322_122_547_200
    vector_router_url: str = ""

    model_config = SettingsConfigDict(env_file=".env", extra="ignore")

    @field_validator("bigquery_max_bytes_billed", mode="after")
    @classmethod
    def _validate_max_bytes(cls, v: int) -> int:
        if v <= 0:
            raise ValueError("bigquery_max_bytes_billed must be a positive byte count")
        return v

    @field_validator("bigquery_date_from", "bigquery_date_to", mode="after")
    @classmethod
    def _validate_date_bounds(cls, v: int) -> int:
        if v != 0 and (v < 19000101 or v > 99991231):
            raise ValueError(
                "Date must be 0 (unbounded) or a valid YYYYMMDD integer "
                "between 19000101 and 99991231"
            )
        return v

    @field_validator("vector_router_url", mode="before")
    @classmethod
    def _reject_blank_url(cls, v: str) -> str:
        if isinstance(v, str) and not v.strip():
            raise ValueError("vector_router_url must not be blank")
        return v
