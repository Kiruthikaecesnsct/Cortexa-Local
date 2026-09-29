import asyncio
import logging

import httpx

from evidence.domain.errors.evidence_errors import PatentApiError
from evidence.domain.models.patent_match import PatentMatch
from evidence.infrastructure.config.secrets_provider import SecretsProvider
from evidence.infrastructure.config.settings import EvidenceSettings
from evidence.infrastructure.patent_apis.patent_adapter import (
    _is_transient,
    build_retry,
    rank_score,
    retry_after_backoff,
)

logger = logging.getLogger(__name__)

_LENS_SEARCH_PATH = "/patent/search"
_LENS_TRIAL_MAX_SIZE = 100


def _extract_localized_text(entries: list) -> str:
    if not entries:
        return ""
    en = next((e.get("text", "") for e in entries if e.get("lang") == "en"), None)
    return en or entries[0].get("text", "")


def _extract_title(biblio: dict) -> str:
    return _extract_localized_text(biblio.get("invention_title", []))


def _extract_applicant(biblio: dict) -> str:
    applicants = biblio.get("parties", {}).get("applicants", [])
    if not applicants:
        return ""
    return applicants[0].get("extracted_name", {}).get("value", "")


def _first_publication_reference(biblio: dict) -> dict:
    refs = biblio.get("publication_reference", [])
    if isinstance(refs, dict):
        refs = [refs]
    if not refs:
        return {}
    return refs[0]


def _extract_reference(biblio: dict) -> str:
    ref = _first_publication_reference(biblio)
    doc_number = ref.get("doc_number", "")
    if not doc_number:
        return ""
    return f"{ref.get('jurisdiction', '')}{doc_number}"


def _extract_jurisdiction(biblio: dict) -> str:
    return _first_publication_reference(biblio).get("jurisdiction", "")


def _extract_abstract(hit: dict) -> str:
    try:
        return _extract_localized_text(hit.get("abstract", []))
    except (AttributeError, TypeError) as exc:
        logger.warning("lens abstract mapping failed: %s", exc)
        return ""


def _extract_claim_text(claim: dict | str) -> str:
    if isinstance(claim, str):
        return claim
    return claim.get("claim_text", "") or claim.get("text", "")


def _extract_claims(hit: dict) -> list[str]:
    try:
        claims = hit.get("claims", [])
        if isinstance(claims, dict):
            claims = claims.get("claim", [])
        return [text for c in claims if (text := _extract_claim_text(c))]
    except (AttributeError, TypeError) as exc:
        logger.warning("lens claims mapping failed: %s", exc)
        return []


def _map_hit(hit: dict, index: int, total: int) -> PatentMatch:
    lens_id = hit.get("lens_id", "")
    biblio = hit.get("biblio", {})
    reference = _extract_reference(biblio) or lens_id
    return PatentMatch(
        reference=reference,
        title=_extract_title(biblio),
        applicant=_extract_applicant(biblio),
        date=hit.get("date_published", ""),
        url=f"https://lens.org/lens/patent/{lens_id}",
        relevance_score=rank_score(index, total),
        jurisdiction=_extract_jurisdiction(biblio),
        abstract=_extract_abstract(hit),
        claims=_extract_claims(hit),
    )


class LensAdapter:
    def __init__(
        self,
        settings: EvidenceSettings,
        secrets: SecretsProvider,
        client: httpx.AsyncClient | None = None,
    ) -> None:
        self._settings = settings
        self._secrets = secrets
        self._client = client or httpx.AsyncClient()

    async def _get_api_key(self) -> str:
        return await self._secrets.get_secret("LENS_API_KEY")

    async def search(self, query: str, limit: int = 20) -> list[PatentMatch]:
        api_key = await self._get_api_key()
        url = self._settings.lens_base.rstrip("/") + _LENS_SEARCH_PATH
        retry_dec = build_retry(self._settings.patent_api_max_retries)
        capped_size = min(limit, _LENS_TRIAL_MAX_SIZE)
        payload = {
            "query": {
                "query_string": {
                    "query": query,
                    "fields": ["biblio.invention_title.text", "abstract", "claims.claim"],
                }
            },
            "size": capped_size,
            "include": ["lens_id", "biblio", "date_published", "abstract", "claims"],
        }

        @retry_dec
        async def _attempt() -> list[dict]:
            try:
                resp = await self._client.post(
                    url,
                    json=payload,
                    headers={
                        "Authorization": f"Bearer {api_key}",
                        "Content-Type": "application/json",
                    },
                    timeout=self._settings.patent_api_timeout_seconds,
                )
                resp.raise_for_status()
                logger.info("lens status=%s url=%s", resp.status_code, url)
                return resp.json().get("data", [])
            except httpx.HTTPStatusError as exc:
                if exc.response.status_code == 429:
                    await retry_after_backoff(exc.response, minimum=8.0)
                if not _is_transient(exc):
                    raise PatentApiError(
                        f"Lens returned {exc.response.status_code}",
                        status_code=exc.response.status_code,
                    ) from exc
                raise
            except asyncio.CancelledError:
                raise
            except Exception as exc:
                if not _is_transient(exc):
                    raise PatentApiError(str(exc)) from exc
                raise

        try:
            hits = await _attempt()
        except PatentApiError:
            raise
        except asyncio.CancelledError:
            raise
        except httpx.HTTPStatusError as exc:
            raise PatentApiError(
                f"Lens returned {exc.response.status_code}",
                status_code=exc.response.status_code,
            ) from exc
        except Exception as exc:
            raise PatentApiError(str(exc)) from exc

        return [_map_hit(h, i, len(hits)) for i, h in enumerate(hits)]
