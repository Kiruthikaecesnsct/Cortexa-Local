import logging
from contextlib import asynccontextmanager

from azure.identity.aio import DefaultAzureCredential
from fastapi import FastAPI

from .api.endpoints import router as api_router
from .application.router import VectorRouter
from .infrastructure.backends.ai_search_backend import AiSearchBackend, AiSearchBackendConfig
from .infrastructure.backends.qdrant_backend import QdrantBackend
from .infrastructure.config.settings import VectorRouterSettings
from .infrastructure.embedding.embedder import Embedder
from .infrastructure.observability.logging_config import configure_logging
from .infrastructure.observability.sentry import configure_sentry

configure_logging()
_logger = logging.getLogger(__name__)


@asynccontextmanager
async def lifespan(app: FastAPI):
    settings = VectorRouterSettings()
    configure_sentry(settings)
    credential = DefaultAzureCredential()

    embedder = Embedder(
        endpoint=settings.embedding_endpoint,
        credential=credential,
        deployment=settings.embedding_deployment,
        dimensions=settings.embedding_dimensions,
        connect_timeout_seconds=settings.embed_connect_timeout_seconds,
        read_timeout_seconds=settings.embed_read_timeout_seconds,
        write_timeout_seconds=settings.embed_write_timeout_seconds,
        pool_timeout_seconds=settings.embed_pool_timeout_seconds,
        api_version=settings.embed_api_version,
    )

    backend = _build_backend(settings, credential, embedder)

    if settings.vector_backend == "ai_search":
        await _ensure_store(backend.ensure_index, "AI Search corpus index")

    await _ensure_store(backend.ensure_asset_store, "asset vector store")

    app.state.vector_router = VectorRouter(backend=backend)
    yield
    await credential.close()


async def _ensure_store(ensure, description: str) -> None:
    try:
        await ensure()
    except Exception:
        _logger.error(
            "Failed to ensure %s exists; continuing startup",
            description,
            exc_info=True,
        )


def _build_backend(
    settings: VectorRouterSettings,
    credential,
    embedder: Embedder,
):
    if settings.vector_backend == "ai_search":
        config = AiSearchBackendConfig(
            endpoint=settings.ai_search_endpoint,
            index_name=settings.ai_search_index_name,
            asset_index_name=settings.ai_search_asset_index_name,
            dimensions=settings.embedding_dimensions,
        )
        return AiSearchBackend(config=config, credential=credential, embedder=embedder)
    if settings.vector_backend == "qdrant":
        return QdrantBackend(
            url=settings.qdrant_url,
            collection_name=settings.qdrant_collection_name,
            asset_collection_name=settings.qdrant_asset_collection_name,
            dimensions=settings.embedding_dimensions,
            embedder=embedder,
        )
    raise ValueError(
        f"Unknown VECTOR_BACKEND: '{settings.vector_backend}'. Must be 'ai_search' or 'qdrant'."
    )


def create_app() -> FastAPI:
    app = FastAPI(title="vector-router", version="0.1.0", lifespan=lifespan)
    app.include_router(api_router)

    @app.get("/health")
    async def health() -> dict[str, str]:
        return {"status": "ok"}

    return app


app = create_app()
