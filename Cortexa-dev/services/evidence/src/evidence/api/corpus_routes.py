import logging

from fastapi import APIRouter, Depends, HTTPException, Request

from evidence.application.dtos.bulk_load_request import BulkLoadRequestDto
from evidence.application.dtos.incremental_add_request import IncrementalAddRequestDto
from evidence.application.dtos.load_corpus_response import LoadCorpusResponseDto
from evidence.application.handlers.load_corpus_handler import LoadCorpusDeps, LoadCorpusHandler
from evidence.domain.errors.evidence_errors import CorpusLoadError

_logger = logging.getLogger(__name__)
# Prefix includes /evidence because the api-gateway forwards the full path
# (no prefix strip on the catch-all route), matching the main evidence router.
router = APIRouter(prefix="/evidence/corpus", tags=["corpus"])


def get_handler(request: Request) -> LoadCorpusHandler:
    deps: LoadCorpusDeps = request.app.state.load_corpus_deps
    return LoadCorpusHandler(deps)


@router.post("/bulk-load", response_model=LoadCorpusResponseDto)
async def bulk_load(
    body: BulkLoadRequestDto,
    handler: LoadCorpusHandler = Depends(get_handler),
) -> LoadCorpusResponseDto:
    try:
        if body.records:
            return await handler.incremental_add(body.records)
        return await handler.bulk_load(body.file_path)
    except CorpusLoadError as exc:
        _logger.error("Corpus bulk load failed: %s", exc)
        raise HTTPException(status_code=exc.status_code or 502, detail=str(exc))


@router.post("/incremental-add", response_model=LoadCorpusResponseDto)
async def incremental_add(
    body: IncrementalAddRequestDto,
    handler: LoadCorpusHandler = Depends(get_handler),
) -> LoadCorpusResponseDto:
    try:
        return await handler.incremental_add(body.records)
    except CorpusLoadError as exc:
        _logger.error("Corpus incremental add failed: %s", exc)
        raise HTTPException(status_code=exc.status_code or 502, detail=str(exc))
