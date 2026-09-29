from dataclasses import dataclass

import httpx

from seeding.domain.errors.seeding_errors import (
    EvidenceServicePermanentError,
    EvidenceServiceTransientError,
)

_SEARCH_ENDPOINT = "/evidence/patents/search"
_BODY_PREVIEW_LEN = 200
_TRANSIENT_STATUS = {429}


def _is_transient_status(status_code: int) -> bool:
    return status_code in _TRANSIENT_STATUS or 500 <= status_code <= 599


@dataclass(frozen=True)
class LivePatentMatch:
    reference: str
    title: str
    applicant: str
    date: str
    url: str
    relevance_score: float
    source: str


@dataclass(frozen=True)
class LiveSourceStatus:
    source: str
    outcome: str
    hit_count: int
    error_detail: str | None


@dataclass(frozen=True)
class EvidenceSearchResult:
    matches: list[LivePatentMatch]
    sources: list[LiveSourceStatus]


def _to_float(value: object) -> float:
    try:
        return float(value)  # type: ignore[arg-type]
    except TypeError, ValueError:
        return 0.0


def _to_int(value: object) -> int:
    try:
        return int(value)  # type: ignore[arg-type]
    except TypeError, ValueError:
        return 0


def _parse_match(raw: dict) -> LivePatentMatch | None:
    reference = str(raw.get("reference") or "").strip()
    if not reference:
        return None
    return LivePatentMatch(
        reference=reference,
        title=str(raw.get("title") or ""),
        applicant=str(raw.get("applicant") or ""),
        date=str(raw.get("date") or ""),
        url=str(raw.get("url") or ""),
        relevance_score=_to_float(raw.get("relevance_score")),
        source=str(raw.get("source") or ""),
    )


def _parse_source(raw: dict) -> LiveSourceStatus:
    outcome = raw.get("outcome") if raw.get("outcome") is not None else raw.get("status")
    error = raw.get("error_detail") if raw.get("error_detail") is not None else raw.get("error")
    return LiveSourceStatus(
        source=str(raw.get("source") or ""),
        outcome=str(outcome or ""),
        hit_count=_to_int(raw.get("hit_count")),
        error_detail=str(error) if error else None,
    )


def _parse_result(data: dict) -> EvidenceSearchResult:
    raw_matches = data.get("matches") or []
    raw_sources = data.get("sources") or []
    matches = [m for raw in raw_matches if (m := _parse_match(raw)) is not None]
    sources = [_parse_source(raw) for raw in raw_sources]
    return EvidenceSearchResult(matches=matches, sources=sources)


class EvidenceClient:
    def __init__(self, base_url: str, timeout: float) -> None:
        self._client = httpx.AsyncClient(base_url=base_url, timeout=timeout)

    async def search_patents(self, query: str, limit: int) -> EvidenceSearchResult:
        data = await self._post(_SEARCH_ENDPOINT, {"query": query, "limit": limit})
        return _parse_result(data)

    async def _post(self, endpoint: str, body: dict) -> dict:
        try:
            response = await self._client.post(endpoint, json=body)
            response.raise_for_status()
            return response.json()
        except httpx.HTTPStatusError as exc:
            self._raise_for_status(endpoint, exc)
        except (httpx.TimeoutException, httpx.NetworkError) as exc:
            raise EvidenceServiceTransientError(
                f"evidence {endpoint} unavailable: {type(exc).__name__}"
            ) from exc

    def _raise_for_status(self, endpoint: str, exc: httpx.HTTPStatusError) -> None:
        status_code = exc.response.status_code
        message = (
            f"evidence {endpoint} returned {status_code}: {exc.response.text[:_BODY_PREVIEW_LEN]}"
        )
        if _is_transient_status(status_code):
            raise EvidenceServiceTransientError(message, status_code=status_code) from exc
        raise EvidenceServicePermanentError(message, status_code=status_code) from exc

    async def aclose(self) -> None:
        await self._client.aclose()
