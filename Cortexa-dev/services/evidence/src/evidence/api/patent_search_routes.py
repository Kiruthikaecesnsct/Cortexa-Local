from fastapi import APIRouter, Depends, Request

from evidence.application.dtos.patent_search_request import PatentSearchRequestDto
from evidence.application.dtos.patent_search_response import (
    PatentMatchDto,
    PatentSearchResponseDto,
    PatentSourceResultDto,
)
from evidence.domain.models.patent_match import PatentMatch
from evidence.domain.models.patent_source_result import PatentSourceResult
from evidence.domain.repositories.composite_patent_adapter_protocol import (
    CompositePatentAdapterProtocol,
)

router = APIRouter(prefix="/evidence/patents", tags=["patents"])


def get_adapter(request: Request) -> CompositePatentAdapterProtocol:
    return request.app.state.patent_adapter


def _to_match_dto(match: PatentMatch) -> PatentMatchDto:
    return PatentMatchDto.model_validate(match.model_dump())


def _to_source_dto(result: PatentSourceResult) -> PatentSourceResultDto:
    return PatentSourceResultDto.model_validate(result.model_dump())


@router.post("/search", response_model=PatentSearchResponseDto)
async def search_patents(
    body: PatentSearchRequestDto,
    adapter: CompositePatentAdapterProtocol = Depends(get_adapter),
) -> PatentSearchResponseDto:
    matches, sources = await adapter.search(body.query, body.limit)
    return PatentSearchResponseDto(
        matches=[_to_match_dto(m) for m in matches],
        sources=[_to_source_dto(s) for s in sources],
    )
