import json
import time
from pathlib import Path
from typing import Any

import httpx
import jwt

OAUTH_SCOPE = "https://www.googleapis.com/auth/cloud-platform"
JWT_GRANT_TYPE = "urn:ietf:params:oauth:grant-type:jwt-bearer"
TOKEN_EXPIRY_SECONDS = 3600
BIGQUERY_API_BASE = "https://bigquery.googleapis.com/bigquery/v2"
REQUEST_TIMEOUT_SECONDS = 60.0


class BigQueryError(Exception):
    pass


class BigQueryAuthError(BigQueryError):
    pass


class BigQueryPermissionError(BigQueryError):
    pass


class BigQueryQueryError(BigQueryError):
    def __init__(self, message: str, status_code: int | None = None) -> None:
        super().__init__(message)
        self.status_code = status_code


class BigQueryTransportError(BigQueryError):
    pass


class BigQueryClient:
    def __init__(self, project_id: str, service_account_path: str) -> None:
        self._project_id = project_id
        self._sa_path = Path(service_account_path)
        self._token: str = ""
        self._token_expiry: float = 0.0
        self._sa_email: str = ""
        self._token_uri: str = ""
        self._private_key: str = ""
        self._load_service_account_key()

    def _load_service_account_key(self) -> None:
        if not self._sa_path.exists():
            raise BigQueryAuthError(f"Service account key file not found: {self._sa_path}")
        try:
            with self._sa_path.open("r") as f:
                key_data = json.load(f)
        except (json.JSONDecodeError, OSError) as e:
            raise BigQueryAuthError(
                f"Failed to read service account key: {type(e).__name__}"
            ) from e
        self._sa_email = key_data.get("client_email", "")
        self._token_uri = key_data.get("token_uri", "")
        self._private_key = key_data.get("private_key", "")
        if not all([self._sa_email, self._token_uri, self._private_key]):
            raise BigQueryAuthError("Service account key missing required fields")

    def _ensure_token(self) -> None:
        now = time.time()
        if self._token and now < self._token_expiry - 60:
            return
        self._refresh_token()

    def _refresh_token(self) -> None:
        now = int(time.time())
        claims = {
            "iss": self._sa_email,
            "scope": OAUTH_SCOPE,
            "aud": self._token_uri,
            "iat": now,
            "exp": now + TOKEN_EXPIRY_SECONDS,
        }
        signed_jwt = jwt.encode(claims, self._private_key, algorithm="RS256")
        payload = {"grant_type": JWT_GRANT_TYPE, "assertion": signed_jwt}
        try:
            resp = httpx.post(
                self._token_uri,
                data=payload,
                timeout=REQUEST_TIMEOUT_SECONDS,
            )
            resp.raise_for_status()
            token_data = resp.json()
            self._token = token_data.get("access_token", "")
            if not self._token:
                raise BigQueryAuthError("Token response missing access_token")
            self._token_expiry = now + token_data.get("expires_in", TOKEN_EXPIRY_SECONDS)
        except httpx.HTTPStatusError as e:
            if e.response.status_code == 401:
                raise BigQueryAuthError("Authentication failed") from e
            raise BigQueryTransportError(f"Token request failed: {e}") from e
        except httpx.RequestError as e:
            raise BigQueryTransportError(f"Token request transport error: {e}") from e

    def run_query(
        self,
        sql: str,
        *,
        dry_run: bool = False,
        max_results: int = 1000,
        max_bytes_billed: int | None = None,
    ) -> dict[str, Any]:
        self._ensure_token()
        url = f"{BIGQUERY_API_BASE}/projects/{self._project_id}/queries"
        body: dict[str, Any] = {
            "query": sql,
            "useLegacySql": False,
            "dryRun": dry_run,
            "maxResults": max_results,
        }
        # maximumBytesBilled is a string int per the BigQuery REST API; BigQuery
        # aborts the query (no charge) if it would scan more than this.
        if max_bytes_billed is not None:
            body["maximumBytesBilled"] = str(max_bytes_billed)
        headers = {"Authorization": f"Bearer {self._token}"}
        try:
            resp = httpx.post(url, json=body, headers=headers, timeout=REQUEST_TIMEOUT_SECONDS)
            resp.raise_for_status()
            data = resp.json()
        except httpx.HTTPStatusError as e:
            self._raise_for_http_error(e)
        except httpx.RequestError as e:
            raise BigQueryTransportError(f"Query request transport error: {e}") from e
        if dry_run:
            return data
        return self._collect_all_rows(data, max_results)

    def _raise_for_http_error(self, e: httpx.HTTPStatusError) -> None:
        status = e.response.status_code
        if status == 401:
            raise BigQueryAuthError("Query authentication failed") from e
        if status == 403:
            raise BigQueryPermissionError("Query permission denied") from e
        if status == 400:
            raise BigQueryQueryError(f"Query syntax or semantic error: {e}", status) from e
        raise BigQueryQueryError(f"Query failed with status {status}: {e}", status) from e

    def _collect_all_rows(self, initial_data: dict[str, Any], max_results: int) -> dict[str, Any]:
        schema_fields = initial_data.get("schema", {}).get("fields", [])
        field_names = [f["name"] for f in schema_fields]
        rows = initial_data.get("rows", [])
        collected = [self._row_to_dict(r, field_names) for r in rows]
        job_ref = initial_data.get("jobReference", {})
        job_id = job_ref.get("jobId", "")
        page_token = initial_data.get("pageToken")
        while page_token and len(collected) < max_results:
            next_batch = self._fetch_page(job_id, page_token, max_results - len(collected))
            page_rows = next_batch.get("rows", [])
            # Guard against a non-empty pageToken paired with zero rows, which
            # would otherwise loop forever without making progress.
            if not page_rows:
                break
            collected.extend(self._row_to_dict(r, field_names) for r in page_rows)
            page_token = next_batch.get("pageToken")
        return {"rows": collected, "schema": {"fields": schema_fields}}

    def _fetch_page(self, job_id: str, page_token: str, max_results: int) -> dict[str, Any]:
        self._ensure_token()
        url = f"{BIGQUERY_API_BASE}/projects/{self._project_id}/queries/{job_id}"
        params = {"pageToken": page_token, "maxResults": max_results}
        headers = {"Authorization": f"Bearer {self._token}"}
        try:
            resp = httpx.get(url, params=params, headers=headers, timeout=REQUEST_TIMEOUT_SECONDS)
            resp.raise_for_status()
            return resp.json()
        except httpx.HTTPStatusError as e:
            self._raise_for_http_error(e)
        except httpx.RequestError as e:
            raise BigQueryTransportError(f"Page fetch transport error: {e}") from e

    def _row_to_dict(self, row: dict[str, Any], field_names: list[str]) -> dict[str, Any]:
        cells = row.get("f", [])
        result: dict[str, Any] = {}
        for idx, name in enumerate(field_names):
            if idx < len(cells):
                result[name] = self._extract_value(cells[idx])
            else:
                result[name] = None
        return result

    def _extract_value(self, cell: dict[str, Any]) -> Any:
        v = cell.get("v")
        if v is None:
            return None
        if isinstance(v, list):
            return [self._extract_value({"v": item}) for item in v]
        if isinstance(v, dict) and "f" in v:
            return [self._extract_value(f) for f in v["f"]]
        return v
