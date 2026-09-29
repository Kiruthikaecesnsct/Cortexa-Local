import logging

from fastapi import APIRouter, Depends, HTTPException, Request

from scoring.application.dtos.score_verdict_request import ScoreVerdictRequestDto
from scoring.application.dtos.store_verdict_request import StoreVerdictRequestDto
from scoring.application.dtos.store_verdict_response import StoreVerdictResponseDto
from scoring.application.handlers.get_verdict_handler import (
    GetVerdictHandler,
    GetVerdictHandlerDeps,
)
from scoring.application.handlers.score_verdict_handler import ScoreVerdictDeps, ScoreVerdictHandler
from scoring.application.handlers.store_verdict_handler import StoreVerdictDeps, StoreVerdictHandler
from scoring.domain.errors.scoring_errors import (
    AxisParseError,
    DualScoringFailedError,
    SingleScoringFailedError,
    UngroundedVerdictError,
)
from scoring.domain.errors.storage_errors import (
    EventPublishError,
    StorageWriteError,
    VerdictNotFoundError,
)

_logger = logging.getLogger(__name__)
router = APIRouter(prefix="/verdicts", tags=["verdicts"])


def get_handler(request: Request) -> StoreVerdictHandler:
    deps: StoreVerdictDeps = request.app.state.store_verdict_deps
    return StoreVerdictHandler(deps)


def get_score_handler(request: Request) -> ScoreVerdictHandler:
    deps: ScoreVerdictDeps = request.app.state.score_verdict_deps
    return ScoreVerdictHandler(deps)


def get_verdict_details_handler(request: Request) -> GetVerdictHandler:
    deps: GetVerdictHandlerDeps = request.app.state.get_verdict_deps
    return GetVerdictHandler(deps)


@router.post("/store", response_model=StoreVerdictResponseDto)
async def store_verdict(
    body: StoreVerdictRequestDto,
    handler: StoreVerdictHandler = Depends(get_handler),
) -> StoreVerdictResponseDto:
    try:
        return await handler.handle(body)
    except EventPublishError as exc:
        _logger.error("Event publish failed: %s", exc)
        raise HTTPException(
            status_code=502,
            detail={
                "error": "event_publish_failed",
                "verdict_id": exc.verdict_id,
                "document_id": exc.document_id,
                "message": str(exc),
            },
        )
    except StorageWriteError as exc:
        _logger.error("Storage write failed: %s", exc)
        raise HTTPException(status_code=500, detail="Verdict storage failed")


@router.post("/score", response_model=StoreVerdictResponseDto)
async def score_verdict(
    body: ScoreVerdictRequestDto,
    handler: ScoreVerdictHandler = Depends(get_score_handler),
) -> StoreVerdictResponseDto:
    try:
        return await handler.handle(body)
    except (AxisParseError, UngroundedVerdictError) as exc:
        _logger.error("Scoring parse failed: %s", exc)
        raise HTTPException(status_code=422, detail={"error": exc.code, "message": str(exc)})
    except (SingleScoringFailedError, DualScoringFailedError) as exc:
        _logger.error("Model scoring failed: %s", exc)
        raise HTTPException(status_code=502, detail={"error": exc.code, "message": str(exc)})
    except EventPublishError as exc:
        _logger.error("Event publish failed: %s", exc)
        raise HTTPException(
            status_code=502,
            detail={
                "error": "event_publish_failed",
                "verdict_id": exc.verdict_id,
                "document_id": exc.document_id,
                "message": str(exc),
            },
        )
    except StorageWriteError as exc:
        _logger.error("Storage write failed: %s", exc)
        raise HTTPException(status_code=500, detail="Verdict storage failed")


@router.get("/{candidate_id}")
async def get_verdict_details(
    candidate_id: str,
    handler: GetVerdictHandler = Depends(get_verdict_details_handler),
) -> dict:
    try:
        detail = await handler.handle(candidate_id)
        return {
            "success": True,
            "data": detail.model_dump(exclude_none=True),
            "correlation_id": candidate_id,
        }
    except VerdictNotFoundError as exc:
        _logger.error("Verdict not found: %s", exc)
        raise HTTPException(
            status_code=404, detail={"error": "verdict_not_found", "message": str(exc)}
        )
