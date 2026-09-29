from fastapi import APIRouter, HTTPException, Request

from ingestion.application.dtos.ingest_request import IngestRequest
from ingestion.application.dtos.ingest_response import IngestResponse
from ingestion.domain.errors.storage_errors import RollbackError, StorageWriteError

router = APIRouter(prefix="/ingestions", tags=["ingestion"])


@router.post("", response_model=IngestResponse, status_code=201)
async def store_ingestion(body: IngestRequest, request: Request) -> IngestResponse:
    handler = request.app.state.store_ingestion_handler
    try:
        return await handler.handle(body)
    except RollbackError:
        raise HTTPException(
            status_code=500,
            detail="Storage rollback failed; state may be inconsistent.",
        )
    except StorageWriteError as exc:
        raise HTTPException(status_code=500, detail=str(exc))
