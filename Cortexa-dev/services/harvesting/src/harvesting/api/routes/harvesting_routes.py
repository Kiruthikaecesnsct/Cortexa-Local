import uuid

from fastapi import APIRouter, Request
from fastapi.responses import JSONResponse

from harvesting.application.dtos.assemble_request import AssembleRequestDto
from harvesting.application.dtos.classify_request import ClassifyRequestDto
from harvesting.application.dtos.rank_request import RankRequestDto
from harvesting.application.handlers.assemble_handler import AssembleDeps, AssembleHandler
from harvesting.application.handlers.classify_handler import ClassifyDeps, ClassifyHandler
from harvesting.application.handlers.get_results_handler import GetResultsDeps, GetResultsHandler
from harvesting.application.handlers.rank_handler import RankDeps, RankHandler
from harvesting.domain.errors.harvesting_errors import (
    AxisMissingError,
    EventPublishError,
    ReportNotFoundError,
    StorageWriteError,
)

router = APIRouter()


def _get_handler(request: Request) -> ClassifyHandler:
    deps: ClassifyDeps = request.app.state.classify_deps
    return ClassifyHandler(deps)


def _get_rank_handler(request: Request) -> RankHandler:
    deps: RankDeps = request.app.state.rank_deps
    return RankHandler(deps)


@router.post("/classify")
async def classify(body: ClassifyRequestDto, request: Request) -> JSONResponse:
    handler = _get_handler(request)
    try:
        result = await handler.handle(body)
        return JSONResponse({"success": True, "data": result.model_dump(mode="json")})
    except AxisMissingError as exc:
        return JSONResponse(
            {"success": False, "error_code": exc.code, "message": str(exc)}, status_code=422
        )
    except StorageWriteError as exc:
        return JSONResponse(
            {"success": False, "error_code": exc.code, "message": str(exc)}, status_code=500
        )


def _get_assemble_handler(request: Request) -> AssembleHandler:
    deps: AssembleDeps = request.app.state.assemble_deps
    return AssembleHandler(deps)


@router.post("/assemble")
async def assemble(body: AssembleRequestDto, request: Request) -> JSONResponse:
    handler = _get_assemble_handler(request)
    try:
        result = await handler.handle(body)
        return JSONResponse({"success": True, "data": result.model_dump(mode="json")})
    except AxisMissingError as exc:
        return JSONResponse(
            {"success": False, "error_code": exc.code, "message": str(exc)}, status_code=422
        )
    except StorageWriteError as exc:
        return JSONResponse(
            {"success": False, "error_code": exc.code, "message": str(exc)}, status_code=500
        )
    except EventPublishError as exc:
        return JSONResponse(
            {"success": False, "error_code": exc.code, "message": str(exc)}, status_code=502
        )


@router.post("/rank")
async def rank(body: RankRequestDto, request: Request) -> JSONResponse:
    handler = _get_rank_handler(request)
    try:
        result = handler.handle(body)
        return JSONResponse({"success": True, "data": result.model_dump(mode="json")})
    except AxisMissingError as exc:
        return JSONResponse(
            {"success": False, "error_code": exc.code, "message": str(exc)}, status_code=422
        )


def _get_results_handler(request: Request) -> GetResultsHandler:
    deps: GetResultsDeps = request.app.state.get_results_deps
    return GetResultsHandler(deps)


@router.get("/batches/{batch_id}/results")
async def get_results(batch_id: str, request: Request) -> JSONResponse:
    handler = _get_results_handler(request)
    correlation_id = str(uuid.uuid4())
    try:
        result = await handler.handle(batch_id)
        return JSONResponse(
            {
                "success": True,
                "data": result.model_dump(mode="json"),
                "correlation_id": correlation_id,
            }
        )
    except ReportNotFoundError as exc:
        return JSONResponse(
            {
                "success": False,
                "error_code": exc.code,
                "message": str(exc),
                "correlation_id": correlation_id,
            },
            status_code=404,
        )
    except StorageWriteError as exc:
        return JSONResponse(
            {
                "success": False,
                "error_code": exc.code,
                "message": str(exc),
                "correlation_id": correlation_id,
            },
            status_code=500,
        )
