import asyncio
import base64
import logging
import random
import time
from collections.abc import Awaitable, Callable

import httpx

from evidence.domain.errors.evidence_errors import PatentApiError, PatentAuthError
from evidence.domain.models.patent_match import PatentMatch
from evidence.infrastructure.config.secrets_provider import SecretsProvider
from evidence.infrastructure.config.settings import EvidenceSettings
from evidence.infrastructure.patent_apis.epo_query_builder import build_epo_cql_query
from evidence.infrastructure.patent_apis.patent_adapter import (
    RateLimiter,
    _is_transient,
    _parse_retry_after,
    build_retry,
    is_epo_throttle_403,
    parse_throttling_control,
    rank_score,
    retry_after_backoff,
)

logger = logging.getLogger(__name__)

_TOKEN_EXPIRY_BUFFER = 60.0
_EPO_SEARCH_PATH = "/3.2/rest-services/published-data/search"
_EPO_BIBLIO_PATH = "/3.2/rest-services/published-data/publication/docdb/biblio"
_EPO_PUBLICATION_DOCDB_PATH = "/3.2/rest-services/published-data/publication/docdb"
_EPO_BIBLIO_BATCH_SIZE = 100
_EPO_ABSTRACT_SUFFIX = "/abstract"
_EPO_CLAIMS_SUFFIX = "/claims"
_THROTTLE_KIND_403 = "403-throttle"
_THROTTLE_KIND_BUSY = "500-busy"
_THROTTLE_BACKOFF_CAP_SECONDS = 10.0


def _extract_text(node: dict | str | list | None) -> str:
    if node is None:
        return ""
    if isinstance(node, str):
        return node
    if isinstance(node, list):
        return _extract_text(node[0] if node else {})
    return str(node.get("$", node.get("#text", "")))


def _select_document_id(document_id: dict | list) -> dict:
    if isinstance(document_id, dict):
        return document_id
    for entry in document_id:
        if entry.get("@document-id-type") == "docdb":
            return entry
    return document_id[0] if document_id else {}


def _dotted_reference(country: str, doc_number: str, kind: str) -> str:
    if not country or not doc_number:
        return ""
    return f"{country}.{doc_number}.{kind}" if kind else f"{country}.{doc_number}"


def _build_docdb_reference(hit: dict) -> str:
    document_id = _select_document_id(hit.get("document-id", {}))
    country = _extract_text(document_id.get("country"))
    doc_number = _extract_text(document_id.get("doc-number"))
    kind = _extract_text(document_id.get("kind"))
    return _dotted_reference(country, doc_number, kind)


def _compact_reference(country: str, doc_number: str, kind: str) -> str:
    return f"{country}{doc_number}{kind}"


def _extract_biblio_title(bib: dict) -> str:
    titles = bib.get("invention-title", [])
    if isinstance(titles, dict):
        titles = [titles]
    if not titles:
        return ""
    chosen = next((t for t in titles if t.get("@lang") == "en"), None) or titles[0]
    return _extract_text(chosen)


def _extract_biblio_applicant(bib: dict) -> str:
    applicants = bib.get("parties", {}).get("applicants", {}).get("applicant", [])
    if isinstance(applicants, dict):
        applicants = [applicants]
    if not applicants:
        return ""
    name_node = applicants[0].get("applicant-name", {}).get("name")
    return _extract_text(name_node)


def _extract_biblio_publication_reference(bib: dict) -> dict:
    pub_ref = bib.get("publication-reference", {}).get("document-id", [])
    if isinstance(pub_ref, list):
        for entry in pub_ref:
            if entry.get("@document-id-type") == "docdb":
                return entry
        return pub_ref[0] if pub_ref else {}
    return pub_ref


def _format_date(date_raw: str) -> str:
    return f"{date_raw[:4]}-{date_raw[4:6]}-{date_raw[6:]}" if len(date_raw) == 8 else date_raw


def _extract_abstract_text(node: dict) -> str:
    paragraphs = node.get("p", node)
    if isinstance(paragraphs, list):
        return " ".join(_extract_text(p) for p in paragraphs).strip()
    return _extract_text(paragraphs).strip()


def _select_lang_node(nodes: list[dict]) -> dict:
    return next((n for n in nodes if n.get("@lang") == "en"), None) or (nodes[0] if nodes else {})


def _as_list(node: dict | list | None) -> list[dict]:
    if node is None:
        return []
    return [node] if isinstance(node, dict) else node


def _extract_abstract(node: dict) -> str:
    abstracts = _as_list(node.get("abstract"))
    if not abstracts:
        return ""
    return _extract_abstract_text(_select_lang_node(abstracts))


def _first_document(node: dict) -> dict:
    documents = _as_list(node)
    return documents[0] if documents else {}


def _map_exchange_document(document: dict, index: int, total: int) -> tuple[PatentMatch, str]:
    bib = document.get("bibliographic-data", {})
    pub_ref = _extract_biblio_publication_reference(bib)
    country = _extract_text(pub_ref.get("country"))
    doc_number = _extract_text(pub_ref.get("doc-number"))
    kind = _extract_text(pub_ref.get("kind"))
    date_raw = _extract_text(pub_ref.get("date"))
    reference = _compact_reference(country, doc_number, kind)
    url = f"https://worldwide.espacenet.com/patent/search?q=pn%3D{doc_number}"
    match = PatentMatch(
        reference=reference,
        title=_extract_biblio_title(bib),
        applicant=_extract_biblio_applicant(bib),
        date=_format_date(date_raw),
        url=url,
        relevance_score=rank_score(index, total),
        jurisdiction=country,
        abstract=_extract_abstract(bib),
    )
    return match, _dotted_reference(country, doc_number, kind)


def _parse_abstract_response(payload: dict) -> str:
    root = payload.get("ops:world-patent-data", {}).get("exchange-documents", {})
    document = _first_document(root.get("exchange-document", {}))
    return _extract_abstract(document)


def _extract_claim_text(claim_node: dict) -> str:
    texts = _as_list(claim_node.get("claim-text"))
    return " ".join(_extract_text(t) for t in texts).strip()


def _parse_claims_response(payload: dict) -> list[str]:
    root = payload.get("ops:world-patent-data", {}).get("ftxt:fulltext-documents", {})
    document = _first_document(root.get("ftxt:fulltext-document", {}))
    claims_by_lang = _as_list(document.get("claims"))
    if not claims_by_lang:
        return []
    chosen = _select_lang_node(claims_by_lang)
    claim_nodes = _as_list(chosen.get("claim"))
    return [text for c in claim_nodes if (text := _extract_claim_text(c))]


def _batches(items: list[str], size: int) -> list[list[str]]:
    return [items[i : i + size] for i in range(0, len(items), size)]


def _throttle_kind(exc: httpx.HTTPStatusError) -> str | None:
    if is_epo_throttle_403(exc.response):
        return _THROTTLE_KIND_403
    if _is_transient(exc):
        return _THROTTLE_KIND_BUSY
    return None


def _no_retry_budget_left(attempt: int, max_retries: int, deadline: float) -> bool:
    return attempt >= max_retries or time.monotonic() >= deadline


def _exponential_backoff_with_jitter(attempt: int) -> float:
    base = min(2**attempt, _THROTTLE_BACKOFF_CAP_SECONDS)
    return random.uniform(0.0, base)


async def _throttle_backoff(response: httpx.Response, attempt: int, deadline: float) -> float:
    delay = _parse_retry_after(response.headers.get("Retry-After"))
    if delay is None:
        delay = _exponential_backoff_with_jitter(attempt)
    delay = min(delay, max(deadline - time.monotonic(), 0.0))
    if delay > 0:
        await asyncio.sleep(delay)
    return delay


def _log_epo_throttle(
    response: httpx.Response, throttle_kind: str, attempt: int, backoff_slept: float
) -> None:
    header_value = response.headers.get("x-throttling-control")
    throttle_info = parse_throttling_control(header_value)
    logger.info(
        "epo_throttle",
        extra={
            "status_code": response.status_code,
            "throttle_kind": throttle_kind,
            "x_throttling_control": header_value,
            "search_colour": throttle_info.search_colour if throttle_info else None,
            "search_limit": throttle_info.search_limit if throttle_info else None,
            "retry_after": response.headers.get("Retry-After"),
            "backoff_slept": round(backoff_slept, 3),
            "attempt": attempt,
        },
    )


class EpoAdapter:
    def __init__(
        self,
        settings: EvidenceSettings,
        secrets: SecretsProvider,
        client: httpx.AsyncClient | None = None,
    ) -> None:
        self._settings = settings
        self._secrets = secrets
        self._client = client or httpx.AsyncClient()
        self._token: str | None = None
        self._token_expires_at: float = 0.0
        self._token_secret_fingerprint: tuple[str, str] | None = None
        self._enrich_semaphore = asyncio.Semaphore(settings.epo_enrich_max_concurrency)
        self._rate_limiter = RateLimiter(rate_per_second=settings.epo_search_max_rps_per_replica)

    async def _current_secret_fingerprint(self) -> tuple[str, str]:
        consumer_key = await self._secrets.get_secret("EPO_CONSUMER_KEY")
        oauth_secret = await self._secrets.get_secret("EPO_OAUTH_SECRET")
        return consumer_key, oauth_secret

    def _token_valid(self, fingerprint: tuple[str, str]) -> bool:
        return (
            self._token is not None
            and time.time() < self._token_expires_at
            and self._token_secret_fingerprint == fingerprint
        )

    async def _fetch_token(self, fingerprint: tuple[str, str] | None = None) -> None:
        consumer_key, oauth_secret = fingerprint or await self._current_secret_fingerprint()
        credentials = base64.b64encode(f"{consumer_key}:{oauth_secret}".encode()).decode()
        token_url = self._settings.epo_base.rstrip("/") + self._settings.epo_token_path
        resp = await self._client.post(
            token_url,
            data={"grant_type": "client_credentials"},
            headers={"Authorization": f"Basic {credentials}"},
            timeout=self._settings.patent_api_timeout_seconds,
        )
        if resp.status_code != 200:
            raise PatentAuthError(
                f"EPO token request failed with {resp.status_code}",
                status_code=resp.status_code,
            )
        body = resp.json()
        self._token = body["access_token"]
        expires_in = float(body.get("expires_in", 1200))
        self._token_expires_at = time.time() + expires_in - _TOKEN_EXPIRY_BUFFER
        self._token_secret_fingerprint = (consumer_key, oauth_secret)
        logger.info("epo token refreshed expires_in=%s", expires_in)

    async def _ensure_token(self) -> str:
        fingerprint = await self._current_secret_fingerprint()
        if not self._token_valid(fingerprint):
            await self._fetch_token(fingerprint)
        return self._token  # type: ignore[return-value]

    async def _execute_attempt(
        self,
        make_request: Callable[[], Awaitable[httpx.Response]],
        url: str,
    ) -> httpx.Response:
        retry_dec = build_retry(self._settings.patent_api_max_retries)

        @retry_dec
        async def _attempt() -> httpx.Response:
            try:
                resp = await make_request()
                return self._raise_for_transient_status(resp, url)
            except httpx.HTTPStatusError as exc:
                if exc.response.status_code == 401:
                    raise
                if exc.response.status_code == 429:
                    await retry_after_backoff(exc.response)
                if not _is_transient(exc):
                    raise PatentApiError(
                        f"EPO returned {exc.response.status_code}",
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

    async def _execute_get_attempt(
        self, bearer: str, url: str, params: dict, extra_headers: dict
    ) -> httpx.Response:
        return await self._execute_attempt(
            lambda: self._client.get(
                url,
                params=params,
                headers={
                    "Authorization": f"Bearer {bearer}",
                    "Accept": "application/json",
                    **extra_headers,
                },
                timeout=self._settings.patent_api_timeout_seconds,
            ),
            url,
        )

    async def _handle_search_attempt_error(
        self, exc: httpx.HTTPStatusError, attempt: int, deadline: float
    ) -> None:
        response = exc.response
        if response.status_code == 401:
            raise exc
        kind = _throttle_kind(exc)
        if kind is None:
            raise PatentApiError(
                f"EPO returned {response.status_code}", status_code=response.status_code
            ) from exc
        max_retries = self._settings.epo_throttle_max_retries
        if _no_retry_budget_left(attempt, max_retries, deadline):
            _log_epo_throttle(response, kind, attempt, 0.0)
            raise PatentApiError(
                f"EPO returned {response.status_code}", status_code=response.status_code
            ) from exc
        backoff_slept = await _throttle_backoff(response, attempt, deadline)
        _log_epo_throttle(response, kind, attempt, backoff_slept)

    async def _execute_search_attempt(
        self,
        make_request: Callable[[], Awaitable[httpx.Response]],
        url: str,
    ) -> httpx.Response:
        deadline = time.monotonic() + self._settings.epo_throttle_retry_budget_seconds
        max_retries = self._settings.epo_throttle_max_retries
        for attempt in range(1, max_retries + 1):
            await self._rate_limiter.acquire()
            try:
                resp = await make_request()
                return self._raise_for_transient_status(resp, url)
            except httpx.HTTPStatusError as exc:
                await self._handle_search_attempt_error(exc, attempt, deadline)
        raise PatentApiError("EPO search retries exhausted")

    async def _execute_search_get_attempt(
        self, bearer: str, url: str, params: dict, extra_headers: dict
    ) -> httpx.Response:
        return await self._execute_search_attempt(
            lambda: self._client.get(
                url,
                params=params,
                headers={
                    "Authorization": f"Bearer {bearer}",
                    "Accept": "application/json",
                    **extra_headers,
                },
                timeout=self._settings.patent_api_timeout_seconds,
            ),
            url,
        )

    async def _execute_post_attempt(
        self, bearer: str, url: str, body: str, extra_headers: dict
    ) -> httpx.Response:
        return await self._execute_attempt(
            lambda: self._client.post(
                url,
                content=body,
                headers={
                    "Authorization": f"Bearer {bearer}",
                    "Accept": "application/json",
                    **extra_headers,
                },
                timeout=self._settings.patent_api_timeout_seconds,
            ),
            url,
        )

    def _raise_for_transient_status(self, resp: httpx.Response, url: str) -> httpx.Response:
        if resp.status_code == 401:
            raise httpx.HTTPStatusError("401 Unauthorized", request=resp.request, response=resp)
        resp.raise_for_status()
        logger.info("epo status=%s url=%s", resp.status_code, url)
        return resp

    async def _search_with_reauth(self, token: str, query: str, url: str, limit: int) -> list[dict]:
        try:
            resp = await self._execute_search_get_attempt(
                token, url, {"q": query}, {"X-OPS-Range": f"1-{limit}"}
            )
            return self._parse_search_response(resp)
        except httpx.HTTPStatusError as exc:
            if exc.response.status_code != 401:
                raise PatentApiError(
                    f"EPO returned {exc.response.status_code}",
                    status_code=exc.response.status_code,
                ) from exc
        # Re-auth once on 401
        await self._fetch_token()
        new_token: str = self._token or ""
        try:
            resp = await self._execute_search_get_attempt(
                new_token, url, {"q": query}, {"X-OPS-Range": f"1-{limit}"}
            )
            return self._parse_search_response(resp)
        except httpx.HTTPStatusError as exc:
            if exc.response.status_code == 401:
                raise PatentAuthError("EPO authentication failed after token refresh", 401) from exc
            raise PatentApiError(
                f"EPO returned {exc.response.status_code}",
                status_code=exc.response.status_code,
            ) from exc

    def _parse_search_response(self, resp: httpx.Response) -> list[dict]:
        results = (
            resp.json()
            .get("ops:world-patent-data", {})
            .get("ops:biblio-search", {})
            .get("ops:search-result", {})
            .get("ops:publication-reference", [])
        )
        return [results] if isinstance(results, dict) else results

    async def _biblio_with_reauth(self, token: str, url: str, refs: list[str]) -> list[dict]:
        body = "\n".join(refs)
        headers = {"Content-Type": "text/plain"}
        try:
            resp = await self._execute_post_attempt(token, url, body, headers)
            return self._parse_biblio_response(resp)
        except httpx.HTTPStatusError as exc:
            if exc.response.status_code != 401:
                raise PatentApiError(
                    f"EPO returned {exc.response.status_code}",
                    status_code=exc.response.status_code,
                ) from exc
        await self._fetch_token()
        new_token: str = self._token or ""
        try:
            resp = await self._execute_post_attempt(new_token, url, body, headers)
            return self._parse_biblio_response(resp)
        except httpx.HTTPStatusError as exc:
            if exc.response.status_code == 401:
                raise PatentAuthError("EPO authentication failed after token refresh", 401) from exc
            raise PatentApiError(
                f"EPO returned {exc.response.status_code}",
                status_code=exc.response.status_code,
            ) from exc

    def _parse_biblio_response(self, resp: httpx.Response) -> list[dict]:
        documents = resp.json().get("exchange-documents", {}).get("exchange-document", [])
        return [documents] if isinstance(documents, dict) else documents

    async def _fetch_biblio_documents(self, token: str, refs: list[str]) -> list[dict]:
        biblio_url = self._settings.epo_base.rstrip("/") + _EPO_BIBLIO_PATH
        documents: list[dict] = []
        current_token = token
        for batch in _batches(refs, _EPO_BIBLIO_BATCH_SIZE):
            documents.extend(await self._biblio_with_reauth(current_token, biblio_url, batch))
            current_token = self._token or current_token
        return documents

    def _enrichment_url(self, pub_ref: str, suffix: str) -> str:
        base = self._settings.epo_base.rstrip("/") + _EPO_PUBLICATION_DOCDB_PATH
        return f"{base}/{pub_ref}{suffix}"

    async def _fetch_enrichment(self, token: str, url: str) -> dict | None:
        try:
            resp = await self._execute_get_attempt(token, url, {}, {})
            return resp.json()
        except asyncio.CancelledError:
            raise
        except PatentApiError as exc:
            if exc.status_code == 404:
                logger.info("epo enrichment empty url=%s", url)
            else:
                logger.warning("epo enrichment failed url=%s error=%s", url, exc)
            return None
        except Exception as exc:
            logger.warning("epo enrichment failed url=%s error=%s", url, exc)
            return None

    async def _apply_abstract(self, token: str, match: PatentMatch, pub_ref: str) -> None:
        abstract_url = self._enrichment_url(pub_ref, _EPO_ABSTRACT_SUFFIX)
        abstract_json = await self._fetch_enrichment(token, abstract_url)
        if not abstract_json:
            return
        try:
            match.abstract = _parse_abstract_response(abstract_json)
        except asyncio.CancelledError:
            raise
        except Exception as exc:
            logger.warning("epo abstract parse failed url=%s error=%s", abstract_url, exc)

    async def _apply_claims(self, token: str, match: PatentMatch, pub_ref: str) -> None:
        claims_url = self._enrichment_url(pub_ref, _EPO_CLAIMS_SUFFIX)
        claims_json = await self._fetch_enrichment(token, claims_url)
        if not claims_json:
            return
        try:
            match.claims = _parse_claims_response(claims_json)
        except asyncio.CancelledError:
            raise
        except Exception as exc:
            logger.warning("epo claims parse failed url=%s error=%s", claims_url, exc)

    async def _enrich_document(self, token: str, match: PatentMatch, pub_ref: str) -> None:
        async with self._enrich_semaphore:
            if not match.abstract:
                await self._apply_abstract(token, match, pub_ref)
            await self._apply_claims(token, match, pub_ref)

    async def _enrich_matches(self, mapped: list[tuple[PatentMatch, str]], token: str) -> None:
        top_n = self._settings.epo_enrich_top_n
        candidates = [(m, ref) for m, ref in mapped[:top_n] if ref]
        if not candidates:
            return
        await asyncio.gather(*(self._enrich_document(token, m, ref) for m, ref in candidates))

    async def search(self, query: str, limit: int = 20) -> list[PatentMatch]:
        cql = build_epo_cql_query(
            query,
            self._settings.epo_query_max_terms,
            self._settings.epo_query_max_length,
        )
        if not cql:
            logger.info("epo skipped: no usable query terms query=%s", query)
            return []

        token = await self._ensure_token()
        search_url = self._settings.epo_base.rstrip("/") + _EPO_SEARCH_PATH
        try:
            hits = await self._search_with_reauth(token, cql, search_url, limit)
        except (PatentApiError, PatentAuthError):  # fmt: skip
            raise
        except asyncio.CancelledError:
            raise
        except Exception as exc:
            raise PatentApiError(str(exc)) from exc

        if not hits:
            return []

        refs = [ref for ref in (_build_docdb_reference(hit) for hit in hits) if ref]
        if not refs:
            return []

        current_token = self._token or token
        try:
            documents = await self._fetch_biblio_documents(current_token, refs)
        except (PatentApiError, PatentAuthError):  # fmt: skip
            raise
        except asyncio.CancelledError:
            raise
        except Exception as exc:
            raise PatentApiError(str(exc)) from exc

        mapped = [_map_exchange_document(d, i, len(documents)) for i, d in enumerate(documents)]
        await self._enrich_matches(mapped, self._token or current_token)
        return [match for match, _ in mapped]
