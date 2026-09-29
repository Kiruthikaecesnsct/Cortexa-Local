from pydantic import field_validator
from pydantic_settings import BaseSettings, SettingsConfigDict


class ScoringSettings(BaseSettings):
    model_router_url: str
    sentry_dsn: str = ""
    sentry_environment: str = "dev"
    sentry_traces_sample_rate: float = 0.1
    cosmos_uri: str
    cosmos_database: str = "cortexa-pipeline"
    verdicts_container: str = "verdicts"
    candidates_container: str = "candidates"
    evidence_container: str = "evidence_bundles"
    seeding_container: str = "seeding"
    batches_container: str = "batches"
    local_dev: bool = False
    dual_mode_enabled: bool = False
    batch_terminal_cache_ttl_seconds: float = 30.0
    model_router_timeout_seconds: float = 90.0
    servicebus_namespace_fqdn: str
    scoring_completed_topic: str = "scoring-completed"
    scoring_failed_topic: str = "scoring-failed"
    scoring_requested_topic: str = "scoring-requested"
    scoring_subscription: str = "scoring"
    consumer_max_attempts: int = 5
    consumer_session_idle_timeout: float = 5.0
    consumer_session_lock_renewal_seconds: float = 1800.0
    consumer_max_concurrent_sessions: int = 6
    max_claim_chars_per_hit: int = 700
    max_abstract_chars_per_hit: int = 300
    claims_per_hit: int = 1
    model_router_max_retries: int = 6
    model_router_backoff_max_seconds: float = 15.0
    model_router_honor_retry_after: bool = True
    model_router_total_retry_budget_seconds: float = 80.0
    claim_draft_timeout_seconds: float = 20.0
    claim_draft_enabled: bool = True

    model_config = SettingsConfigDict(env_file=".env", extra="ignore")

    @field_validator("cosmos_uri", "servicebus_namespace_fqdn", "model_router_url", mode="before")
    @classmethod
    def _reject_blank(cls, v: str, info: object) -> str:
        if isinstance(v, str) and not v.strip():
            field_name = getattr(info, "field_name", "field")
            raise ValueError(
                f"{field_name} must not be blank — set the corresponding environment variable"
            )
        return v
