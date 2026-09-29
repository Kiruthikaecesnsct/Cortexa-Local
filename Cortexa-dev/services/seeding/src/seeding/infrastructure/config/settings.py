from pydantic import field_validator
from pydantic_settings import BaseSettings, SettingsConfigDict


class SeedingSettings(BaseSettings):
    model_router_url: str
    vector_router_url: str
    vector_router_timeout_seconds: float = 60.0
    model_router_timeout_seconds: float = 165.0
    model_router_max_retries: int = 6
    model_router_backoff_max_seconds: float = 15.0
    model_router_honor_retry_after: bool = True
    model_router_total_retry_budget_seconds: float = 60.0
    sentry_dsn: str = ""
    sentry_environment: str = "dev"
    sentry_traces_sample_rate: float = 0.1
    applicationinsights_connection_string: str | None = None
    seeding_task_kind: str = "seeding"
    seeding_candidates_per_call: int = 15
    seeding_max_output_tokens: int = 16384
    local_dev: bool = False
    servicebus_namespace_fqdn: str = ""
    engine_completed_topic: str = "engine-completed"
    seeding_failed_topic: str = "seeding-failed"
    ideation_completed_topic: str = "ideation-completed"
    seeding_report_requested_topic: str = "seeding-report-requested"
    seeding_report_subscription: str = "seeding"
    engine_name: str = "seeding"
    cosmos_uri: str = ""
    cosmos_database: str = "cortexa-pipeline"
    seeding_container: str = "seeding"
    seeding_requested_topic: str = "seeding-requested"
    seeding_subscription: str = "seeding"
    consumer_max_attempts: int = 5
    consumer_session_idle_timeout: float = 5.0
    consumer_session_lock_renewal_seconds: float = 1800.0
    cosmos_container_reports: str = "reports"
    cosmos_container_candidates: str = "candidates"
    cosmos_container_verdicts: str = "verdicts"
    cosmos_container_evidence_bundles: str = "evidence_bundles"
    chunks_container: str = "chunks"
    batches_container: str = "batches"
    batch_terminal_cache_ttl_seconds: float = 30.0
    asset_embedding_requested_topic: str = "asset-embedding-requested"
    asset_embedding_subscription: str = "seeding"
    asset_embedding_completed_topic: str = "asset-embedding-completed"
    embed_batch_size: int = 16
    upsert_batch_size: int = 100
    digest_requested_topic: str = "digest-requested"
    digest_completed_topic: str = "digest-completed"
    digest_subscription: str = "seeding"
    digest_task_kind: str = "seeding_digest"
    digest_prompt_version: str = "1.0.0"
    digest_schema_version: str = "1.0"
    digest_map_max_group_tokens: int = 6000
    digest_max_output_tokens: int = 4096
    digest_reduce_max_input_tokens: int = 12000
    digest_reduce_max_depth: int = 3
    digest_reduce_max_fan: int = 8
    digest_message_time_budget_seconds: float = 1200.0
    evidence_url: str
    evidence_timeout_seconds: float = 60.0
    landscape_requested_topic: str = "landscape-requested"
    landscape_completed_topic: str = "landscape-completed"
    landscape_subscription: str = "seeding"
    landscape_schema_version: str = "1.0"
    landscape_top_k: int = 25
    landscape_whitespace_top_k: int = 5
    landscape_embed_max_texts: int = 96
    landscape_live_query_maxlen: int = 256
    landscape_live_limit: int = 25
    landscape_search_concurrency: int = 4
    landscape_sim_floor: float = 0.65
    landscape_crowded_max_sim: float = 0.82
    landscape_moderate_max_sim: float = 0.70
    landscape_crowded_corpus_hits: int = 10
    landscape_moderate_corpus_hits: int = 3
    landscape_crowded_live_hits: int = 15
    landscape_moderate_live_hits: int = 3
    landscape_whitespace_sim: float = 0.62
    landscape_message_time_budget_seconds: float = 1200.0
    seeding_mode_default: str = "legacy"
    ideation_task_kind: str = "seeding"
    ideation_prompt_version: str = "1.0.0"
    ideation_max_rounds: int = 6
    ideation_ideas_per_round: int = 5
    ideation_retrieval_top_k: int = 12
    ideation_themes_per_round: int = 3
    ideation_excerpt_char_limit: int = 600
    ideation_roadmap_char_limit: int = 2000
    ideation_scratchpad_summary_char_limit: int = 2000
    ideation_propose_max_input_tokens: int = 14000
    ideation_critique_max_input_tokens: int = 10000
    ideation_refine_max_input_tokens: int = 12000
    ideation_max_output_tokens: int = 4096
    ideation_message_time_budget_seconds: float = 1200.0
    ideation_schema_version: str = "1.0"

    model_config = SettingsConfigDict(env_file=".env", extra="ignore")

    @field_validator("model_router_url", "vector_router_url", "evidence_url", mode="before")
    @classmethod
    def _reject_blank(cls, v: str, info: object) -> str:
        if isinstance(v, str) and not v.strip():
            field_name = getattr(info, "field_name", "field")
            raise ValueError(
                f"{field_name} must not be blank — set the corresponding environment variable"
            )
        return v
