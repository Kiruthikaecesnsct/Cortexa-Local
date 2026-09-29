import asyncio
import logging

import httpx

from evidence.domain.errors.evidence_errors import PatentApiError, PatentAuthError
from evidence.domain.models.patent_match import PatentMatch
from evidence.domain.services.patent_url import canonical_google_patents_url
from evidence.infrastructure.config.secrets_provider import SecretsProvider
from evidence.infrastructure.config.settings import EvidenceSettings
from evidence.infrastructure.patent_apis.patent_adapter import (
    RateLimiter,
    _is_transient,
    build_retry,
    rank_score,
    retry_after_backoff,
)

logger = logging.getLogger(__name__)

_USPTO_SEARCH_PATH = "/api/v1/patent/applications/search"
_USPTO_PAGE_SIZE_CAP = 100
_USPTO_RETRY_AFTER_FLOOR_SECONDS = 5.0
_USPTO_SEARCH_FIELDS = [
    "applicationNumberText",
    "applicationMetaData.inventionTitle",
    "applicationMetaData.filingDate",
    "applicationMetaData.grantDate",
    "applicationMetaData.patentNumber",
    "applicationMetaData.firstApplicantName",
    "applicationMetaData.firstInventorName",
    "applicationMetaData.applicationStatusDescriptionText",
    "applicationMetaData.cpcClassificationBag",
    "applicationMetaData.earliestPublicationNumber",
]


def _normalize_hits(raw: dict | list | None) -> list[dict]:
    if raw is None:
        return []
    if isinstance(raw, dict):
        return [raw]
    return raw


def _normalize_applicant(raw: str | list | None) -> str:
    if isinstance(raw, list):
        return raw[0] if raw else ""
    return raw or ""


def _map_hit(hit: dict, index: int, total: int) -> PatentMatch:
    meta = hit.get("applicationMetaData") or {}
    patent_number = meta.get("patentNumber") or ""
    reference = (
        meta.get("earliestPublicationNumber")
        or patent_number
        or hit.get("applicationNumberText", "")
    )
    date = meta.get("grantDate") or meta.get("filingDate") or ""
    return PatentMatch(
        reference=reference,
        title=meta.get("inventionTitle") or "",
        applicant=_normalize_applicant(meta.get("firstApplicantName")),
        date=date,
        url=canonical_google_patents_url(reference),
        relevance_score=rank_score(index, total),
        jurisdiction="US",
    )


def _document_abstract(doc_meta: dict) -> str:
    return doc_meta.get("abstractText") or doc_meta.get("inventionTitle") or ""


def _extract_abstract(payload: dict | None) -> str:
    hits = _normalize_hits((payload or {}).get("patentFileWrapperDataBag"))
    if not hits:
        return ""
    hit = hits[0]
    grant = hit.get("grantDocumentMetaData") or {}
    pgpub = hit.get("pgpubDocumentMetaData") or {}
    return _document_abstract(grant) or _document_abstract(pgpub)


def _normalize_claim_text(entry: dict | str) -> str:
    if isinstance(entry, dict):
        return str(entry.get("claimText") or entry.get("text") or "").strip()
    if isinstance(entry, str):
        return entry.strip()
    return ""


def _parse_claims_bag(raw: str | dict | list | None) -> list[str]:
    if raw is None:
        return []
    if isinstance(raw, list):
        return [text for text in (_normalize_claim_text(entry) for entry in raw) if text]
    text = _normalize_claim_text(raw)
    return [text] if text else []


def _extract_claims(payload: dict | None) -> list[str]:
    hits = _normalize_hits((payload or {}).get("patentFileWrapperDataBag"))
    if not hits:
        return []
    grant = hits[0].get("grantDocumentMetaData") or {}
    return _parse_claims_bag(grant.get("claimTextBag"))


def _build_search_payload(query: str, limit: int) -> dict:
    return {
        "q": query,
        "pagination": {"offset": 0, "limit": min(limit, _USPTO_PAGE_SIZE_CAP)},
        "fields": _USPTO_SEARCH_FIELDS,
    }


class UsptoAdapter:
    def __init__(
        self,
        settings: EvidenceSettings,
        secrets: SecretsProvider,
        client: httpx.AsyncClient | None = None,
    ) -> None:
        self._settings = settings
        self._secrets = secrets
        self._client = client or httpx.AsyncClient()
        self._rate_limiter = RateLimiter(rate_per_second=settings.uspto_search_max_rps_per_replica)

    async def _get_api_key(self) -> str:
        return await self._secrets.get_secret("USPTO_API_KEY")

    async def _dispatch_request(self, url: str, payload: dict, api_key: str) -> httpx.Response:
        await self._rate_limiter.acquire()
        resp = await self._client.post(
            url,
            json=payload,
            headers={
                "X-API-KEY": api_key,
                "Accept": "application/json",
                "Content-Type": "application/json",
            },
            timeout=self._settings.patent_api_timeout_seconds,
        )
        resp.raise_for_status()
        logger.info("uspto status=%s url=%s", resp.status_code, url)
        return resp

    async def _dispatch_get(self, url: str, api_key: str) -> httpx.Response:
        await self._rate_limiter.acquire()
        resp = await self._client.get(
            url,
            headers={"X-API-KEY": api_key, "Accept": "application/json"},
            timeout=self._settings.patent_api_timeout_seconds,
        )
        logger.info("uspto status=%s url=%s", resp.status_code, url)
        return resp

    async def _fetch_json_or_none(self, url: str, api_key: str) -> dict | None:
        try:
            resp = await self._dispatch_get(url, api_key)
        except asyncio.CancelledError:
            raise
        except (httpx.TimeoutException, httpx.NetworkError) as exc:
            logger.warning("uspto enrichment request error url=%s error=%s", url, exc)
            return None
        if resp.status_code == 404:
            return None
        if resp.status_code >= 400:
            logger.warning("uspto enrichment status=%s url=%s", resp.status_code, url)
            return None
        try:
            return resp.json()
        except ValueError:
            logger.warning("uspto enrichment invalid json url=%s", url)
            return None

    async def _enrich_match(self, match: PatentMatch, app_number: str, api_key: str) -> None:
        if not app_number:
            return
        base = self._settings.uspto_base.rstrip("/")
        abstract_payload = await self._fetch_json_or_none(
            f"{base}/api/v1/patent/applications/{app_number}/associated-documents", api_key
        )
        if abstract_payload is not None:
            match.abstract = _extract_abstract(abstract_payload)
        claims_payload = await self._fetch_json_or_none(
            f"{base}/api/v1/patent/applications/{app_number}", api_key
        )
        if claims_payload is not None:
            match.claims = _extract_claims(claims_payload)

    async def _try_enrich(self, match: PatentMatch, app_number: str, api_key: str) -> None:
        try:
            await self._enrich_match(match, app_number, api_key)
        except asyncio.CancelledError:
            raise
        except Exception as exc:
            logger.warning("uspto enrichment failed app=%s error=%s", app_number, exc)

    async def _enrich_top_matches(
        self, matches: list[PatentMatch], hits: list[dict], api_key: str
    ) -> None:
        top_n = self._settings.uspto_enrich_top_n
        for match, hit in list(zip(matches, hits, strict=False))[:top_n]:
            await self._try_enrich(match, hit.get("applicationNumberText", ""), api_key)

    async def _execute_search_attempt(self, url: str, payload: dict, api_key: str) -> list[dict]:
        retry_dec = build_retry(self._settings.patent_api_max_retries)

        @retry_dec
        async def _attempt() -> list[dict]:
            try:
                resp = await self._dispatch_request(url, payload, api_key)
                return _normalize_hits(resp.json().get("patentFileWrapperDataBag"))
            except httpx.HTTPStatusError as exc:
                if exc.response.status_code in {401, 403}:
                    raise PatentAuthError(
                        f"USPTO returned {exc.response.status_code}",
                        status_code=exc.response.status_code,
                    ) from exc
                if exc.response.status_code == 429:
                    await retry_after_backoff(
                        exc.response, minimum=_USPTO_RETRY_AFTER_FLOOR_SECONDS
                    )
                if not _is_transient(exc):
                    raise PatentApiError(
                        f"USPTO returned {exc.response.status_code}",
                        status_code=exc.response.status_code,
                    ) from exc
                raise
            except asyncio.CancelledError:
                raise
            except Exception as exc:
                if not _is_transient(exc):
                    raise PatentApiError(str(exc)) from exc
                raise

        return await _attempt()

    async def search(self, query: str, limit: int = 20) -> list[PatentMatch]:
        api_key = await self._get_api_key()
        url = self._settings.uspto_base.rstrip("/") + _USPTO_SEARCH_PATH
        payload = _build_search_payload(query, limit)

        try:
            hits = await self._execute_search_attempt(url, payload, api_key)
        except PatentApiError:
            raise
        except asyncio.CancelledError:
            raise
        except httpx.HTTPStatusError as exc:
            if exc.response.status_code in {401, 403}:
                raise PatentAuthError(
                    f"USPTO returned {exc.response.status_code}",
                    status_code=exc.response.status_code,
                ) from exc
            raise PatentApiError(
                f"USPTO returned {exc.response.status_code}",
                status_code=exc.response.status_code,
            ) from exc
        except Exception as exc:
            raise PatentApiError(str(exc)) from exc

        matches = [_map_hit(h, i, len(hits)) for i, h in enumerate(hits)]
        await self._enrich_top_matches(matches, hits, api_key)
        return matches
