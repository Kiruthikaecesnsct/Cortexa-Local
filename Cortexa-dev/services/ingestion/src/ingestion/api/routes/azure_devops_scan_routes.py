from fastapi import APIRouter, Request
from fastapi.responses import JSONResponse

from ingestion.api.routes.scan_responses import respond
from ingestion.application.dtos.azure_devops_scan_dtos import (
    ListBranchesRequest,
    ListRepositoriesRequest,
    RepositoryTreeRequest,
)
from ingestion.application.handlers.azure_devops_scan_handler import AzureDevOpsScanHandler

router = APIRouter(prefix="/scan/azure-devops", tags=["scan"])


def _handler(request: Request) -> AzureDevOpsScanHandler:
    return request.app.state.azure_devops_scan_handler


@router.post("/repositories")
async def list_repositories(body: ListRepositoriesRequest, request: Request) -> JSONResponse:
    return await respond(request, _handler(request).list_repositories(body))


@router.post("/branches")
async def list_branches(body: ListBranchesRequest, request: Request) -> JSONResponse:
    return await respond(request, _handler(request).list_branches(body))


@router.post("/tree")
async def get_tree(body: RepositoryTreeRequest, request: Request) -> JSONResponse:
    return await respond(request, _handler(request).get_tree(body))
