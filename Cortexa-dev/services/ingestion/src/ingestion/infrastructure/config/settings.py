from typing import Literal

from pydantic_settings import BaseSettings


class IngestionSettings(BaseSettings):
    service_name: str = "ingestion"
    port: int = 8000

    sentry_dsn: str = ""
    sentry_environment: str = "dev"
    sentry_traces_sample_rate: float = 0.1

    keyvault_uri: str = ""
    # Set to true only in local development; forces env-var fallback when keyvault_uri is empty.
    local_dev: bool = False
    github_token_secret_name: str = "github-pat"
    azdo_pat_secret_name: str = "azdo-pat"

    clone_max_repo_bytes: int = 500 * 1024 * 1024
    clone_timeout_seconds: float = 120.0
    clone_max_concurrency: int = 4
    clone_workdir: str = "/tmp/cortexa-clones"
    clone_use_ssh: bool = False

    chunk_size_tokens: int = 512
    chunk_overlap_tokens: int = 50
    chunk_encoding: str = "cl100k_base"

    cosmos_uri: str = ""
    # Primary key for local/emulator Cosmos auth; empty uses DefaultAzureCredential.
    cosmos_key: str = ""
    cosmos_database: str = "cortexa-pipeline"
    provenance_container: str = "provenance_maps"
    documents_container: str = "documents"
    chunks_container: str = "chunks"
    batches_container: str = "batches"

    blob_account_url: str = ""
    # Connection string for local/emulator Blob auth (e.g. Azurite); empty uses managed identity.
    blob_connection_string: str = ""
    blob_raw_container: str = "raw-files"
    blob_viewable_container: str = "viewable-docs"
    raw_file_max_bytes: int = 100 * 1024 * 1024  # 100 MB

    docx_convert_timeout_seconds: float = 60.0
    geometry_enabled: bool = True
    geometry_max_words_per_page: int = 3000

    # "servicebus" uses Azure Service Bus; "rabbitmq" uses a local RabbitMQ broker.
    messaging_backend: Literal["servicebus", "rabbitmq"] = "servicebus"
    # AMQP URL for the rabbitmq backend (amqp://user:password@host:port/vhost).
    rabbitmq_url: str = ""
    servicebus_namespace_fqdn: str = ""
    # Connection string for local/emulator Service Bus auth; empty uses DefaultAzureCredential.
    servicebus_connection_string: str = ""
    ingestion_completed_topic: str = "ingestion-completed"
    ingestion_requested_topic: str = "ingestion-requested"
    ingestion_subscription: str = "ingestion"
    servicebus_sessions_enabled: bool = True
    consumer_max_attempts: int = 5
    consumer_session_idle_timeout: float = 5.0

    batch_terminal_cache_ttl_seconds: float = 30.0

    # GitHub scan uses only the PAT the user supplies per request, never github_token_secret_name.
    github_api_base_url: str = "https://api.github.com"
    github_api_version: str = "2026-03-10"
    github_scan_timeout_seconds: float = 20.0
    github_scan_max_pages: int = 50

    # Azure DevOps scan uses only the PAT the user supplies per request, never
    # azdo_pat_secret_name.
    azdo_api_base_url: str = "https://dev.azure.com"
    azdo_api_version: str = "7.1"
    azdo_scan_timeout_seconds: float = 20.0

    model_config = {"env_prefix": "", "env_file": ".env", "extra": "ignore"}
