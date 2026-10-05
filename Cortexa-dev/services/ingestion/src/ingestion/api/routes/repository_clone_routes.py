from dataclasses import dataclass
from urllib.parse import quote

from fastapi import APIRouter, Query, Request
from fastapi.responses import JSONResponse, Response, StreamingResponse
from pydantic import BaseModel

from ingestion.api.routes.scan_responses import (
    USER_ID_HEADER,
    correlation_id,
    failure,
    respond,
)
from ingestion.application.dtos.azure_devops_scan_dtos import (
    RepositoryTreeRequest as AzureDevOpsSaveRequest,
)
from ingestion.application.dtos.github_scan_dtos import CloneFilesResponse, CloneListResponse
from ingestion.application.dtos.github_scan_dtos import (
    RepositoryTreeRequest as GitHubSaveRequest,
)
from ingestion.application.handlers.repository_clone_handler import RepositoryCloneHandler
from ingestion.domain.errors.scan_errors import ScanError

_ACCEPTED = 202
_MAX_OWNER = 64
_MAX_REPOSITORY = 129
_MAX_BRANCH = 255


@dataclass(frozen=True)
class CloneRouteConfig:
    prefix: str
    handler_attr: str
    request_model: type[BaseModel]


def _user_id(request: Request) -> str | None:
    # Set by the api-gateway from the validated JWT; the gateway strips any client-sent value.
    return request.headers.get(USER_ID_HEADER)


def _content_disposition(filename: str) -> str:
    # Repository names may hold spaces, quotes or non-ASCII characters: send an ASCII
    # fallback plus the RFC 5987 encoded name so the header can never be broken.
    fallback = "".join(
        c if c.isascii() and c.isprintable() and c not in '"\\' else "_" for c in filename
    )
    return f"attachment; filename=\"{fallback}\"; filename*=UTF-8''{quote(filename)}"


async def _download_response(
    handler: RepositoryCloneHandler, request: Request, names: tuple[str, str, str]
) -> Response:
    try:
        download = await handler.open_download(_user_id(request), *names)
    except ScanError as exc:
        return failure(exc, correlation_id(request))
    return StreamingResponse(
        download.chunks,
        media_type="application/zip",
        headers={
            "Content-Disposition": _content_disposition(download.filename),
            "Content-Length": str(download.size_bytes),
        },
    )


def build_clone_router(config: CloneRouteConfig) -> APIRouter:
    router = APIRouter(prefix=config.prefix, tags=["scan"])
    request_model = config.request_model

    def handler(request: Request) -> RepositoryCloneHandler:
        return getattr(request.app.state, config.handler_attr)

    async def listed(request: Request) -> CloneListResponse:
        return CloneListResponse(clones=await handler(request).list_clones(_user_id(request)))

    async def listed_files(
        request: Request, owner: str, repository: str, branch: str
    ) -> CloneFilesResponse:
        files = await handler(request).list_files(_user_id(request), owner, repository, branch)
        return CloneFilesResponse(files=files)

    @router.post("")
    async def start_clone(body: request_model, request: Request) -> JSONResponse:  # type: ignore[valid-type]
        operation = handler(request).start(body, _user_id(request))
        return await respond(request, operation, status_code=_ACCEPTED)

    @router.get("")
    async def list_clones(request: Request) -> JSONResponse:
        return await respond(request, listed(request))

    @router.get("/files")
    async def get_files(
        request: Request,
        owner: str = Query(max_length=_MAX_OWNER),
        repository: str = Query(max_length=_MAX_REPOSITORY),
        branch: str = Query(max_length=_MAX_BRANCH),
    ) -> JSONResponse:
        return await respond(request, listed_files(request, owner, repository, branch))

    @router.get("/download", response_model=None)
    async def download_clone(
        request: Request,
        owner: str = Query(max_length=_MAX_OWNER),
        repository: str = Query(max_length=_MAX_REPOSITORY),
        branch: str = Query(max_length=_MAX_BRANCH),
    ) -> Response:
        return await _download_response(handler(request), request, (owner, repository, branch))

    return router


github_clone_router = build_clone_router(
    CloneRouteConfig("/scan/github/clones", "repository_clone_handler", GitHubSaveRequest)
)
azure_devops_clone_router = build_clone_router(
    CloneRouteConfig(
        "/scan/azure-devops/clones", "azure_devops_clone_handler", AzureDevOpsSaveRequest
    )
)
