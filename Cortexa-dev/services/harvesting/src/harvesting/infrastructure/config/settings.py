from typing import Literal

from pydantic import field_validator
from pydantic_settings import BaseSettings, SettingsConfigDict


class HarvestingSettings(BaseSettings):
    sentry_dsn: str = ""
    sentry_environment: str = "dev"
    sentry_traces_sample_rate: float = 0.1
    cosmos_uri: str
    # Primary key for local/emulator Cosmos auth; empty uses DefaultAzureCredential.
    cosmos_key: str = ""
    cosmos_database: str = "cortexa-pipeline"
    cosmos_container_harvesting: str = "harvesting"
    maturity_novelty_threshold: int = 70
    maturity_feasibility_threshold: int = 60
    local_dev: bool = False
    ranker_weight_novelty: float = 0.4
    ranker_weight_feasibility: float = 0.25
    ranker_weight_strategic: float = 0.2
    ranker_weight_patentability: float = 0.15
    cosmos_container_reports: str = "reports"
    cosmos_container_verdicts: str = "verdicts"
    # "servicebus" uses Azure Service Bus; "rabbitmq" uses a local RabbitMQ broker.
    messaging_backend: Literal["servicebus", "rabbitmq"] = "servicebus"
    # AMQP URL for the rabbitmq backend (amqp://user:password@host:port/vhost).
    rabbitmq_url: str = ""
    servicebus_namespace_fqdn: str
    # Connection string for local/emulator Service Bus auth; empty uses DefaultAzureCredential.
    servicebus_connection_string: str = ""
    servicebus_topic_engine_completed: str = "engine-completed"
    harvesting_requested_topic: str = "harvesting-requested"
    harvesting_failed_topic: str = "harvesting-failed"
    harvesting_subscription: str = "harvesting"
    consumer_max_attempts: int = 5
    consumer_session_idle_timeout: float = 5.0
    consumer_session_lock_renewal_seconds: float = 1800.0
    cosmos_container_candidates: str = "candidates"
    cosmos_container_evidence: str = "evidence_bundles"
    cosmos_container_chunks: str = "chunks"
    cosmos_container_documents: str = "documents"
    cosmos_container_provenance: str = "provenance_maps"
    batches_container: str = "batches"
    batch_terminal_cache_ttl_seconds: float = 30.0
    cosmos_report_write_concurrency: int = 4
    cosmos_retry_throttle_total: int = 12
    cosmos_retry_throttle_backoff_max: int = 60

    model_config = SettingsConfigDict(env_file=".env", extra="ignore")

    @field_validator("cosmos_uri", "servicebus_namespace_fqdn", mode="before")
    @classmethod
    def _reject_blank(cls, v: str, info: object) -> str:
        if isinstance(v, str) and not v.strip():
            field_name = getattr(info, "field_name", "field")
            raise ValueError(
                f"{field_name} must not be blank — set the corresponding environment variable"
            )
        return v
