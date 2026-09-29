from typing import Literal

from pydantic_settings import BaseSettings, SettingsConfigDict


class VectorRouterSettings(BaseSettings):
    model_config = SettingsConfigDict(env_file=".env", env_file_encoding="utf-8")

    vector_backend: Literal["ai_search", "qdrant"] = "ai_search"

    sentry_dsn: str = ""
    sentry_environment: str = "dev"
    sentry_traces_sample_rate: float = 0.1

    ai_search_endpoint: str = ""
    ai_search_index_name: str = "cortexa-corpus"
    ai_search_asset_index_name: str = "cortexa-asset"

    qdrant_url: str = ""
    qdrant_collection_name: str = "cortexa-corpus"
    qdrant_asset_collection_name: str = "cortexa-asset"

    embedding_endpoint: str = ""
    embedding_deployment: str = ""
    embedding_dimensions: int = 1536
    embed_connect_timeout_seconds: float = 10.0
    embed_read_timeout_seconds: float = 60.0
    embed_write_timeout_seconds: float = 10.0
    embed_pool_timeout_seconds: float = 10.0
    embed_api_version: str = "2024-10-21"
