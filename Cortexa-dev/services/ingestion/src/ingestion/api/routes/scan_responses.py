import uuid
from collections.abc import Awaitable

from fastapi import Request
from fastapi.responses import JSONResponse
from pydantic import BaseModel

from ingestion.domain.errors.scan_errors import ScanError

CORRELATION_HEADER = "X-Correlation-Id"
USER_ID_HEADER = "X-User-Id"
_UPSTREAM_STATUS = 502
# Keyed by error code so GitHub, Azure DevOps and save errors share one table.
# A rejected token maps to 400, never 401: the frontend treats 401 as an expired
# Cortexa session and would sign the user out.
_ERROR_STATUS: dict[str, int] = {
    "invalid_scan_target": 422,
    "repository_too_large": 422,
    "github_auth_failed": 400,
    "azure_devops_auth_failed": 400,
    "github_access_denied": 403,
    "azure_devops_access_denied": 403,
    "missing_user_context": 403,
    "github_not_found": 404,
    "azure_devops_not_found": 404,
    "clone_not_found": 404,
    "github_rate_limited": 429,
    "azure_devops_rate_limited": 429,
    "clone_storage_unavailable": 503,
}


def correlation_id(request: Request) -> str:
    return request.headers.get(CORRELATION_HEADER) or str(uuid.uuid4())


def success(data: BaseModel, cid: str, status_code: int = 200) -> JSONResponse:
    return JSONResponse(
        {"success": True, "data": data.model_dump(mode="json"), "correlation_id": cid},
        status_code=status_code,
    )


def failure(exc: ScanError, cid: str) -> JSONResponse:
    return JSONResponse(
        {"success": False, "error_code": exc.code, "message": str(exc), "correlation_id": cid},
        status_code=_ERROR_STATUS.get(exc.code, _UPSTREAM_STATUS),
    )


async def respond(
    request: Request, operation: Awaitable[BaseModel], status_code: int = 200
) -> JSONResponse:
    cid = correlation_id(request)
    try:
        result = await operation
    except ScanError as exc:
        return failure(exc, cid)
    return success(result, cid, status_code)
