from unittest.mock import AsyncMock, patch

import pytest

from vector_router.infrastructure.backends.ai_search_backend import AiSearchBackend
from vector_router.infrastructure.backends.qdrant_backend import QdrantBackend
from vector_router.infrastructure.config.settings import VectorRouterSettings
from vector_router.main import _build_backend, lifespan


def test_build_backend_ai_search_returns_ai_search_backend(
    fake_settings_ai_search, mock_credential, mock_embedder
):
    backend = _build_backend(fake_settings_ai_search, mock_credential, mock_embedder)

    assert isinstance(backend, AiSearchBackend)


def test_build_backend_qdrant_returns_qdrant_backend(
    fake_settings_qdrant, mock_credential, mock_embedder
):
    backend = _build_backend(fake_settings_qdrant, mock_credential, mock_embedder)

    assert isinstance(backend, QdrantBackend)


def test_build_backend_unknown_value_raises_value_error(mock_credential, mock_embedder):
    settings = VectorRouterSettings(vector_backend="ai_search")
    object.__setattr__(settings, "vector_backend", "pinecone")

    with pytest.raises(ValueError, match="pinecone"):
        _build_backend(settings, mock_credential, mock_embedder)


def test_build_backend_ai_search_passes_dimensions_from_settings(
    fake_settings_ai_search, mock_credential, mock_embedder
):
    backend = _build_backend(fake_settings_ai_search, mock_credential, mock_embedder)

    assert backend._config.dimensions == fake_settings_ai_search.embedding_dimensions


async def test_lifespan_ensure_index_exception_swallowed_for_ai_search(
    fake_settings_ai_search,
):
    mock_app = AsyncMock()
    mock_backend = AsyncMock()
    mock_backend.ensure_index = AsyncMock(side_effect=RuntimeError("Azure throttling"))

    mock_credential = AsyncMock()
    mock_credential.close = AsyncMock()

    with (
        patch("vector_router.main.VectorRouterSettings", return_value=fake_settings_ai_search),
        patch("vector_router.main._build_backend", return_value=mock_backend),
        patch("vector_router.main.DefaultAzureCredential", return_value=mock_credential),
        patch("vector_router.main.Embedder"),
        patch("vector_router.main.configure_sentry"),
    ):
        async with lifespan(mock_app):
            pass

    mock_backend.ensure_index.assert_awaited_once()
    assert hasattr(mock_app.state, "vector_router")


async def test_lifespan_ensures_asset_store_for_qdrant(fake_settings_qdrant):
    mock_app = AsyncMock()
    mock_backend = AsyncMock()
    mock_backend.ensure_asset_store = AsyncMock()

    mock_credential = AsyncMock()
    mock_credential.close = AsyncMock()

    with (
        patch("vector_router.main.VectorRouterSettings", return_value=fake_settings_qdrant),
        patch("vector_router.main._build_backend", return_value=mock_backend),
        patch("vector_router.main.DefaultAzureCredential", return_value=mock_credential),
        patch("vector_router.main.Embedder"),
        patch("vector_router.main.configure_sentry"),
    ):
        async with lifespan(mock_app):
            pass

    mock_backend.ensure_asset_store.assert_awaited_once()
    mock_backend.ensure_index.assert_not_called()
