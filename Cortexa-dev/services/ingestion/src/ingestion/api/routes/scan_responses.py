import uuid
from collections.abc import Awaitable

from fastapi import Request
from fastapi.responses import JSONResponse
from pydantic import BaseModel

from ingestion.domain.errors.github_scan_errors import (
    CloneNotFoundError,
    CloneStorageUnavailableError,
    GitHubAccessDeniedError,
    GitHubAuthError,
    GitHubNotFoundError,
    GitHubRateLimitError,
    GitHubScanError,
    InvalidScanTargetError,
    MissingUserContextError,
    RepositoryTooLargeError,
)

CORRELATION_HEADER = "X-Correlation-Id"
USER_ID_HEADER = "X-User-Id"
_UPSTREAM_STATUS = 502
# A rejected GitHub token maps to 400, never 401: the frontend treats 401 as an
# expired Cortexa session and would sign the user out.
_ERROR_STATUS: dict[type[GitHubScanError], int] = {
    InvalidScanTargetError: 422,
    RepositoryTooLargeError: 422,
    GitHubAuthError: 400,
    GitHubAccessDeniedError: 403,
    MissingUserContextError: 403,
    GitHubNotFoundError: 404,
    CloneNotFoundError: 404,
    GitHubRateLimitError: 429,
    CloneStorageUnavailableError: 503,
}


def correlation_id(request: Request) -> str:
    return request.headers.get(CORRELATION_HEADER) or str(uuid.uuid4())


def success(data: BaseModel, cid: str, status_code: int = 200) -> JSONResponse:
    return JSONResponse(
        {"success": True, "data": data.model_dump(mode="json"), "correlation_id": cid},
        status_code=status_code,
    )


def failure(exc: GitHubScanError, cid: str) -> JSONResponse:
    return JSONResponse(
        {"success": False, "error_code": exc.code, "message": str(exc), "correlation_id": cid},
        status_code=_ERROR_STATUS.get(type(exc), _UPSTREAM_STATUS),
    )


async def respond(
    request: Request, operation: Awaitable[BaseModel], status_code: int = 200
) -> JSONResponse:
    cid = correlation_id(request)
    try:
        result = await operation
    except GitHubScanError as exc:
        return failure(exc, cid)
    return success(result, cid, status_code)
