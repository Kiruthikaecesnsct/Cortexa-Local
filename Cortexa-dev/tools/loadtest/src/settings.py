from pydantic import AnyHttpUrl
from pydantic_settings import BaseSettings, SettingsConfigDict


class LoadTestSettings(BaseSettings):
    model_config = SettingsConfigDict(
        env_prefix="CORTEXA_",
        env_file=".env",
        env_file_encoding="utf-8",
        extra="ignore",
    )

    gateway_base_url: AnyHttpUrl = AnyHttpUrl(
        "https://cortexa-dev-api-gateway.proudsmoke-86efe866.eastus.azurecontainerapps.io"
    )
    key_vault_name: str = "cortexa-dev-kv"
    admin_email_secret: str = "identity-admin-email"
    admin_password_secret: str = "identity-admin-initial-password"
    default_ai_model: str = "gpt-4o"
    seed_corpus_domain: str = "software"

    document_count: int = 100
    documents_per_batch: int = 10
    poll_timeout_s: int = 6000
    poll_interval_s: int = 20

    resource_group_name: str = "cortexa-dev-rg"
    container_apps_environment: str = "cortexa-dev-cae"
    log_analytics_workspace_id: str = ""
    app_insights_connection_string: str = ""
