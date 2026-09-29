import asyncio
import random

import httpx
from azure.identity.aio import get_bearer_token_provider

_COGNITIVE_SERVICES_SCOPE = "https://cognitiveservices.azure.com/.default"
_RETRYABLE_STATUS = frozenset({429, 500, 502, 503, 504})
_RETRYABLE_EXCEPTIONS = (httpx.TimeoutException, httpx.NetworkError)


class Embedder:
    def __init__(
        self,
        endpoint: str,
        credential,
        deployment: str,
        dimensions: int,
        max_retries: int = 6,
        backoff_base_seconds: float = 1.0,
        backoff_max_seconds: float = 30.0,
        connect_timeout_seconds: float = 10.0,
        read_timeout_seconds: float = 60.0,
        write_timeout_seconds: float = 10.0,
        pool_timeout_seconds: float = 10.0,
        api_version: str = "2024-10-21",
    ) -> None:
        self._endpoint = endpoint
        self._token_provider = get_bearer_token_provider(credential, _COGNITIVE_SERVICES_SCOPE)
        self._deployment = deployment
        self._dimensions = dimensions
        self._max_retries = max_retries
        self._backoff_base_seconds = backoff_base_seconds
        self._backoff_max_seconds = backoff_max_seconds
        self._timeout = httpx.Timeout(
            connect=connect_timeout_seconds,
            read=read_timeout_seconds,
            write=write_timeout_seconds,
            pool=pool_timeout_seconds,
        )
        self._api_version = api_version

    async def embed(self, texts: list[str]) -> list[list[float]]:
        payload = {"input": texts, "dimensions": self._dimensions}
        async with httpx.AsyncClient(timeout=self._timeout) as client:
            for attempt in range(self._max_retries + 1):
                try:
                    response = await self._post(client, payload)
                    if (
                        response.status_code not in _RETRYABLE_STATUS
                        or attempt == self._max_retries
                    ):
                        response.raise_for_status()
                        data = response.json()
                        return [item["embedding"] for item in data["data"]]
                    await asyncio.sleep(self._retry_delay(response, attempt))
                except _RETRYABLE_EXCEPTIONS:
                    if attempt == self._max_retries:
                        raise
                    await asyncio.sleep(self._exception_backoff(attempt))
        raise RuntimeError("unreachable: retry loop exited without returning")

    async def _post(self, client: httpx.AsyncClient, payload: dict) -> httpx.Response:
        token = await self._token_provider()
        url = (
            f"{self._endpoint}/openai/deployments/{self._deployment}"
            f"/embeddings?api-version={self._api_version}"
        )
        return await client.post(
            url,
            json=payload,
            headers={
                "Authorization": f"Bearer {token}",
                "Content-Type": "application/json",
            },
        )

    def _retry_delay(self, response: httpx.Response, attempt: int) -> float:
        retry_after = _parse_retry_after(response.headers.get("Retry-After"))
        if retry_after is not None:
            return min(retry_after, self._backoff_max_seconds)
        return _calculate_exponential_backoff(
            attempt, self._backoff_base_seconds, self._backoff_max_seconds
        )

    def _exception_backoff(self, attempt: int) -> float:
        base = _calculate_exponential_backoff(
            attempt, self._backoff_base_seconds, self._backoff_max_seconds
        )
        return random.uniform(0, base)


def _calculate_exponential_backoff(attempt: int, base_seconds: float, max_seconds: float) -> float:
    return min(base_seconds * (2**attempt), max_seconds)


def _parse_retry_after(value: str | None) -> float | None:
    if not value:
        return None
    try:
        return float(value)
    except ValueError:
        return None
