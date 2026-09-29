from unittest.mock import AsyncMock

import pytest

from vector_router.infrastructure.config.settings import VectorRouterSettings


@pytest.fixture
def fake_settings_ai_search() -> VectorRouterSettings:
    return VectorRouterSettings(
        vector_backend="ai_search",
        ai_search_endpoint="https://test-search.search.windows.net",
        ai_search_index_name="test-index",
        ai_search_asset_index_name="test-asset-index",
        qdrant_url="",
        qdrant_collection_name="test-corpus",
        qdrant_asset_collection_name="test-asset",
        embedding_endpoint="https://test-oai.openai.azure.com",
        embedding_deployment="text-embedding-ada-002",
        embedding_dimensions=3,
    )


@pytest.fixture
def fake_settings_qdrant() -> VectorRouterSettings:
    return VectorRouterSettings(
        vector_backend="qdrant",
        ai_search_endpoint="",
        ai_search_index_name="test-index",
        ai_search_asset_index_name="test-asset-index",
        qdrant_url="http://localhost:6333",
        qdrant_collection_name="test-corpus",
        qdrant_asset_collection_name="test-asset",
        embedding_endpoint="https://test-oai.openai.azure.com",
        embedding_deployment="text-embedding-ada-002",
        embedding_dimensions=3,
    )


@pytest.fixture
def mock_credential() -> AsyncMock:
    credential = AsyncMock()
    credential.get_token = AsyncMock(return_value=AsyncMock(token="test-token"))
    return credential


@pytest.fixture
def mock_embedder() -> AsyncMock:
    embedder = AsyncMock()
    embedder.embed = AsyncMock(return_value=[[0.1, 0.1, 0.1]])
    return embedder
