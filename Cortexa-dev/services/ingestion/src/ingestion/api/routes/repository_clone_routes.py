from fastapi import APIRouter, Query, Request
from fastapi.responses import JSONResponse, Response, StreamingResponse

from ingestion.api.routes.scan_responses import (
    USER_ID_HEADER,
    correlation_id,
    failure,
    respond,
)
from ingestion.application.dtos.github_scan_dtos import CloneListResponse, RepositoryTreeRequest
from ingestion.application.handlers.repository_clone_handler import RepositoryCloneHandler
from ingestion.domain.errors.github_scan_errors import GitHubScanError
from ingestion.domain.models.repository_clone import RepositoryClone

router = APIRouter(prefix="/scan/github/clones", tags=["scan"])

_ACCEPTED = 202


def _handler(request: Request) -> RepositoryCloneHandler:
    return request.app.state.repository_clone_handler


def _user_id(request: Request) -> str | None:
    # Set by the api-gateway from the validated JWT; the gateway strips any client-sent value.
    return request.headers.get(USER_ID_HEADER)


async def _list(request: Request) -> CloneListResponse:
    clones = await _handler(request).list_clones(_user_id(request))
    return CloneListResponse(clones=clones)


async def _start(body: RepositoryTreeRequest, request: Request) -> RepositoryClone:
    return await _handler(request).start(body, _user_id(request))


@router.post("")
async def start_clone(body: RepositoryTreeRequest, request: Request) -> JSONResponse:
    return await respond(request, _start(body, request), status_code=_ACCEPTED)


@router.get("")
async def list_clones(request: Request) -> JSONResponse:
    return await respond(request, _list(request))


@router.get("/download", response_model=None)
async def download_clone(
    request: Request,
    owner: str = Query(max_length=39),
    repository: str = Query(max_length=100),
    branch: str = Query(max_length=255),
) -> Response:
    try:
        download = await _handler(request).open_download(
            _user_id(request), owner, repository, branch
        )
    except GitHubScanError as exc:
        return failure(exc, correlation_id(request))
    return StreamingResponse(
        download.chunks,
        media_type="application/zip",
        headers={
            "Content-Disposition": f'attachment; filename="{download.filename}"',
            "Content-Length": str(download.size_bytes),
        },
    )
