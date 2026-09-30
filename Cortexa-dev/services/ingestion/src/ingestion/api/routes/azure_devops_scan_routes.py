import uuid
from collections.abc import Awaitable

from fastapi import APIRouter, Request
from fastapi.responses import JSONResponse
from pydantic import BaseModel

from ingestion.application.dtos.azure_devops_scan_dtos import (
    ListBranchesRequest,
    ListRepositoriesRequest,
    RepositoryTreeRequest,
)
from ingestion.application.handlers.azure_devops_scan_handler import AzureDevOpsScanHandler
from ingestion.domain.errors.azure_devops_scan_errors import (
    AzureDevOpsAccessDeniedError,
    AzureDevOpsAuthError,
    AzureDevOpsNotFoundError,
    AzureDevOpsRateLimitError,
    AzureDevOpsScanError,
    InvalidScanTargetError,
)

router = APIRouter(prefix="/scan/azure-devops", tags=["scan"])

_CORRELATION_HEADER = "X-Correlation-Id"
_UPSTREAM_STATUS = 502
# A rejected PAT maps to 400, never 401: the frontend treats 401 as an expired
# Cortexa session and would sign the user out.
_ERROR_STATUS: dict[type[AzureDevOpsScanError], int] = {
    InvalidScanTargetError: 422,
    AzureDevOpsAuthError: 400,
    AzureDevOpsAccessDeniedError: 403,
    AzureDevOpsNotFoundError: 404,
    AzureDevOpsRateLimitError: 429,
}


def _handler(request: Request) -> AzureDevOpsScanHandler:
    return request.app.state.azure_devops_scan_handler


def _correlation_id(request: Request) -> str:
    return request.headers.get(_CORRELATION_HEADER) or str(uuid.uuid4())


def _success(data: BaseModel, correlation_id: str) -> JSONResponse:
    return JSONResponse(
        {"success": True, "data": data.model_dump(mode="json"), "correlation_id": correlation_id}
    )


def _failure(exc: AzureDevOpsScanError, correlation_id: str) -> JSONResponse:
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
    except AzureDevOpsScanError as exc:
        return _failure(exc, correlation_id)
    return _success(result, correlation_id)


@router.post("/repositories")
async def list_repositories(body: ListRepositoriesRequest, request: Request) -> JSONResponse:
    return await _respond(request, _handler(request).list_repositories(body))


@router.post("/branches")
async def list_branches(body: ListBranchesRequest, request: Request) -> JSONResponse:
    return await _respond(request, _handler(request).list_branches(body))


@router.post("/tree")
async def get_tree(body: RepositoryTreeRequest, request: Request) -> JSONResponse:
    return await _respond(request, _handler(request).get_tree(body))
