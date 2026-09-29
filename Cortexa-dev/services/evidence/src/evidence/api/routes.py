import logging

from fastapi import APIRouter, Depends, HTTPException, Request

from evidence.application.dtos.store_evidence_request import StoreEvidenceRequestDto
from evidence.application.dtos.store_evidence_response import StoreEvidenceResponseDto
from evidence.application.handlers.store_evidence_handler import (
    StoreEvidenceDeps,
    StoreEvidenceHandler,
)
from evidence.domain.errors.evidence_errors import StorageWriteError

_logger = logging.getLogger(__name__)
router = APIRouter(prefix="/evidence", tags=["evidence"])


def get_handler(request: Request) -> StoreEvidenceHandler:
    deps: StoreEvidenceDeps = request.app.state.store_evidence_deps
    return StoreEvidenceHandler(deps)


@router.post("/store", response_model=StoreEvidenceResponseDto)
async def store_evidence(
    body: StoreEvidenceRequestDto,
    handler: StoreEvidenceHandler = Depends(get_handler),
) -> StoreEvidenceResponseDto:
    try:
        return await handler.handle(body)
    except StorageWriteError as exc:
        _logger.error("Storage write failed: %s", exc)
        raise HTTPException(status_code=500, detail=str(exc))
