import uuid

from fastapi import APIRouter, Request
from fastapi.responses import JSONResponse

from seeding.application.dtos.generate_claim_seeds_request import GenerateClaimSeedsRequest
from seeding.application.dtos.generate_idf_request import GenerateIdfRequest
from seeding.application.dtos.generate_lattice_request import GenerateLatticeRequest
from seeding.application.dtos.generate_map_request import GenerateMapRequest
from seeding.application.dtos.seeding_result_response import SeedingResultResponse
from seeding.application.handlers import (
    generate_claim_seeds_handler,
    generate_idf_handler,
    generate_lattice_handler,
    generate_map_handler,
)
from seeding.application.handlers.generate_claim_seeds_handler import GenerateClaimSeedsDeps
from seeding.application.handlers.generate_idf_handler import GenerateIdfDeps
from seeding.application.handlers.generate_lattice_handler import GenerateLatticeDeps
from seeding.application.handlers.generate_map_handler import GenerateMapDeps
from seeding.domain.errors.seeding_errors import (
    ModelRouterFailedError,
    SeedingError,
    SeedingReportNotFoundError,
    StorageWriteError,
)
from seeding.domain.models.landscape import LandscapeSourceFlags

router = APIRouter()


def _handle_seeding_error(exc: SeedingError) -> JSONResponse:
    status = 502 if isinstance(exc, ModelRouterFailedError) else 422
    return JSONResponse(
        {"success": False, "error_code": exc.code, "message": str(exc)}, status_code=status
    )


def _get_deps(request: Request) -> GenerateMapDeps:
    return request.app.state.generate_map_deps


def _get_idf_deps(request: Request) -> GenerateIdfDeps:
    return request.app.state.generate_idf_deps


def _get_claim_seeds_deps(request: Request) -> GenerateClaimSeedsDeps:
    return request.app.state.generate_claim_seeds_deps


def _get_lattice_deps(request: Request) -> GenerateLatticeDeps:
    return request.app.state.generate_lattice_deps


@router.post("/generate-map")
async def generate_map(body: GenerateMapRequest, request: Request) -> JSONResponse:
    deps = _get_deps(request)
    try:
        result = await generate_map_handler.handle(deps, body)
        return JSONResponse({"success": True, "data": result.model_dump(mode="json")})
    except SeedingError as exc:
        return _handle_seeding_error(exc)


@router.post("/generate-claim-seeds")
async def generate_claim_seeds(body: GenerateClaimSeedsRequest, request: Request) -> JSONResponse:
    deps = _get_claim_seeds_deps(request)
    try:
        result = await generate_claim_seeds_handler.handle(deps, body)
        return JSONResponse({"success": True, "data": result.model_dump(mode="json")})
    except SeedingError as exc:
        return _handle_seeding_error(exc)


@router.post("/generate-lattice")
async def generate_lattice(body: GenerateLatticeRequest, request: Request) -> JSONResponse:
    deps = _get_lattice_deps(request)
    try:
        result = await generate_lattice_handler.handle(deps, body)
        return JSONResponse({"success": True, "data": result.model_dump(mode="json")})
    except SeedingError as exc:
        return _handle_seeding_error(exc)


@router.post("/generate-idf")
async def generate_idf(body: GenerateIdfRequest, request: Request) -> JSONResponse:
    deps = _get_idf_deps(request)
    try:
        result = await generate_idf_handler.handle(deps, body)
        return JSONResponse({"success": True, "data": result.model_dump(mode="json")})
    except SeedingError as exc:
        return _handle_seeding_error(exc)


@router.get("/batches/{batch_id}/results")
async def get_seeding_results(batch_id: str, request: Request) -> JSONResponse:
    correlation_id = str(uuid.uuid4())
    seeding_report_repository = request.app.state.seeding_report_repository
    try:
        seeding_result = await seeding_report_repository.get_by_batch(batch_id)
        landscape = seeding_result.landscape
        response_data = SeedingResultResponse(
            id=seeding_result.id,
            batch_id=seeding_result.batch_id,
            opportunities=seeding_result.opportunities,
            created_at=seeding_result.created_at,
            engine=seeding_result.engine,
            document_id=seeding_result.document_id,
            landscape=landscape,
            source_flags=landscape.source_flags if landscape else LandscapeSourceFlags(),
            concept_map=seeding_result.concept_map,
            explanation=seeding_result.explanation,
            is_empty=seeding_result.is_empty,
        )
        return JSONResponse(
            {
                "success": True,
                "data": response_data.model_dump(mode="json"),
                "correlation_id": correlation_id,
            }
        )
    except SeedingReportNotFoundError:
        return JSONResponse(
            {
                "success": False,
                "error_code": "SEEDING_RESULT_NOT_FOUND",
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
