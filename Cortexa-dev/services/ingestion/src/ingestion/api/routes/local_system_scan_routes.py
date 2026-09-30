import uuid
from collections.abc import Awaitable

from fastapi import APIRouter, Request
from fastapi.responses import JSONResponse
from pydantic import BaseModel

from ingestion.application.dtos.local_system_scan_dtos import ListDirectoryRequest
from ingestion.application.handlers.local_system_scan_handler import LocalSystemScanHandler
from ingestion.domain.errors.local_system_scan_errors import (
    InvalidScanTargetError,
    LocalSystemAccessDeniedError,
    LocalSystemAuthError,
    LocalSystemConnectionError,
    LocalSystemPathNotFoundError,
    LocalSystemScanError,
    LocalSystemTimeoutError,
)

router = APIRouter(prefix="/scan/local-system", tags=["scan"])

_CORRELATION_HEADER = "X-Correlation-Id"
_UPSTREAM_STATUS = 502
# A rejected key maps to 400, never 401: the frontend treats 401 as an expired
# Cortexa session and would sign the user out.
_ERROR_STATUS: dict[type[LocalSystemScanError], int] = {
    InvalidScanTargetError: 422,
    LocalSystemAuthError: 400,
    LocalSystemConnectionError: 502,
    LocalSystemPathNotFoundError: 404,
    LocalSystemAccessDeniedError: 403,
    LocalSystemTimeoutError: 504,
}


def _handler(request: Request) -> LocalSystemScanHandler:
    return request.app.state.local_system_scan_handler


def _correlation_id(request: Request) -> str:
    return request.headers.get(_CORRELATION_HEADER) or str(uuid.uuid4())


def _success(data: BaseModel, correlation_id: str) -> JSONResponse:
    return JSONResponse(
        {"success": True, "data": data.model_dump(mode="json"), "correlation_id": correlation_id}
    )


def _failure(exc: LocalSystemScanError, correlation_id: str) -> JSONResponse:
    return JSONResponse(
        {
            "success": False,
            "error_code": exc.code,
            "message": str(exc),
            "correlation_id": correlation_id,
        },
        status_code=_ERROR_STATUS.get(type(exc), _UPSTREAM_STATUS),
    )


async def _respond(request: Request, operation: Awaitable[BaseModel]) -> JSONResponse:
    correlation_id = _correlation_id(request)
    try:
        result = await operation
    except LocalSystemScanError as exc:
        return _failure(exc, correlation_id)
    return _success(result, correlation_id)


@router.post("/list")
async def list_directory(body: ListDirectoryRequest, request: Request) -> JSONResponse:
    return await _respond(request, _handler(request).list_directory(body))
