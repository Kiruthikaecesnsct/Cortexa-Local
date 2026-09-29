from typing import Literal

from pydantic_settings import BaseSettings

from extraction.domain.enums.model_mode import ModelMode


class ExtractionSettings(BaseSettings):
    model_router_url: str
    model_call_timeout_seconds: float = 90.0
    model_call_max_retries: int = 6
    model_call_backoff_max_seconds: float = 10.0
    model_call_honor_retry_after: bool = True
    model_call_total_retry_budget_seconds: float = 200.0
    model_max_output_tokens: int = 8192
    extraction_concurrency: int = 4
    session_lock_renewal_seconds: float = 1800.0
    max_chunks_per_unit: int = 25
    model_default_mode: ModelMode = ModelMode.single_primary
    sentry_dsn: str = ""
    sentry_environment: str = "dev"
    sentry_traces_sample_rate: float = 0.1
    keyvault_uri: str = ""
    local_dev: bool = False
    cosmos_uri: str = ""
    # Primary key for local/emulator Cosmos auth; empty uses DefaultAzureCredential.
    cosmos_key: str = ""
    cosmos_database: str = "cortexa-pipeline"
    candidates_container: str = "candidates"
    batches_container: str = "batches"
    batch_terminal_cache_ttl_seconds: float = 30.0
    # "servicebus" uses Azure Service Bus; "rabbitmq" uses a local RabbitMQ broker.
    messaging_backend: Literal["servicebus", "rabbitmq"] = "servicebus"
    # AMQP URL for the rabbitmq backend (amqp://user:password@host:port/vhost).
    rabbitmq_url: str = ""
    servicebus_namespace_fqdn: str = ""
    # Connection string for local/emulator Service Bus auth; empty uses DefaultAzureCredential.
    servicebus_connection_string: str = ""
    extraction_completed_topic: str = "extraction-completed"
    extraction_failed_topic: str = "extraction-failed"
    extraction_requested_topic: str = "extraction-requested"
    extraction_subscription: str = "extraction"
    servicebus_sessions_enabled: bool = True
    consumer_max_attempts: int = 5
    consumer_session_idle_timeout: float = 5.0
    chunks_container: str = "chunks"
    documents_container: str = "documents"
    max_excerpt_chars: int = 400

    model_config = {"env_prefix": "", "env_file": ".env", "extra": "ignore"}
