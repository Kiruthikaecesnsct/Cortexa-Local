from fastapi import APIRouter, HTTPException, Request

from extraction.application.dtos.store_extraction_request import StoreExtractionRequest
from extraction.application.dtos.store_extraction_response import StoreExtractionResponse
from extraction.domain.errors.storage_errors import RollbackError, StorageWriteError

router = APIRouter(prefix="/extractions", tags=["extraction"])


@router.post("", response_model=StoreExtractionResponse, status_code=201)
async def store_extraction(
    body: StoreExtractionRequest, request: Request
) -> StoreExtractionResponse:
    handler = request.app.state.store_extraction_handler
    try:
        return await handler.handle(body)
    except RollbackError:
        raise HTTPException(
            status_code=500,
            detail="Storage rollback failed; state may be inconsistent.",
        )
    except StorageWriteError:
        raise HTTPException(status_code=500, detail="Storage operation failed.")
