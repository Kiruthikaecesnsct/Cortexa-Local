import httpx

from seeding.domain.errors.seeding_errors import (
    VectorRouterPermanentError,
    VectorRouterTransientError,
)

_EMBED_ENDPOINT = "/embed"
_UPSERT_ENDPOINT = "/upsert"
_SEARCH_ENDPOINT = "/search"
_BODY_PREVIEW_LEN = 200
_TRANSIENT_STATUS = {429}


def _is_transient_status(status_code: int) -> bool:
    return status_code in _TRANSIENT_STATUS or 500 <= status_code <= 599


class VectorRouterClient:
    def __init__(self, base_url: str, timeout: float) -> None:
        self._client = httpx.AsyncClient(base_url=base_url, timeout=timeout)

    async def embed(self, texts: list[str]) -> list[list[float]]:
        data = await self._post(_EMBED_ENDPOINT, {"texts": texts})
        return data["embeddings"]

    async def upsert(self, items: list[dict], target: str = "asset") -> int:
        data = await self._post(_UPSERT_ENDPOINT, {"items": items, "target": target})
        return data["upserted"]

    async def search(
        self,
        embedding: list[float],
        top_k: int,
        target: str,
        filters: list[dict] | None = None,
    ) -> list[dict]:
        body = {"embedding": embedding, "top_k": top_k, "filters": filters, "target": target}
        data = await self._post(_SEARCH_ENDPOINT, body)
        return data["hits"]

    async def _post(self, endpoint: str, body: dict) -> dict:
        try:
            response = await self._client.post(endpoint, json=body)
            response.raise_for_status()
            return response.json()
        except httpx.HTTPStatusError as exc:
            self._raise_for_status(endpoint, exc)
        except (httpx.TimeoutException, httpx.NetworkError) as exc:
            raise VectorRouterTransientError(
                f"vector-router {endpoint} unavailable: {type(exc).__name__}"
            ) from exc

    def _raise_for_status(self, endpoint: str, exc: httpx.HTTPStatusError) -> None:
        status_code = exc.response.status_code
        message = (
            f"vector-router {endpoint} returned {status_code}: "
            f"{exc.response.text[:_BODY_PREVIEW_LEN]}"
        )
        if _is_transient_status(status_code):
            raise VectorRouterTransientError(message, status_code=status_code) from exc
        raise VectorRouterPermanentError(message, status_code=status_code) from exc

    async def aclose(self) -> None:
        await self._client.aclose()
