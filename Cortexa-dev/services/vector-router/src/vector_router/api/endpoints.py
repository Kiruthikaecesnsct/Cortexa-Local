from fastapi import APIRouter, Depends

from .dependencies import get_router
from .schemas import (
    AssetDeleteResponse,
    EmbedRequest,
    EmbedResponse,
    SearchRequest,
    SearchResponse,
    UpsertRequest,
    UpsertResponse,
)

router = APIRouter()


@router.post("/search", response_model=SearchResponse)
async def search(req: SearchRequest, vr=Depends(get_router)) -> SearchResponse:
    hits = await vr.search(req.embedding, req.top_k, req.filters, target=req.target)
    return SearchResponse(hits=hits)


@router.post("/upsert", response_model=UpsertResponse)
async def upsert(req: UpsertRequest, vr=Depends(get_router)) -> UpsertResponse:
    count = await vr.upsert(req.items, target=req.target)
    return UpsertResponse(upserted=count)


@router.delete("/asset/{batch_id}", response_model=AssetDeleteResponse)
async def delete_asset(batch_id: str, vr=Depends(get_router)) -> AssetDeleteResponse:
    deleted = await vr.delete_asset_batch(batch_id)
    return AssetDeleteResponse(deleted=deleted)


@router.post("/embed", response_model=EmbedResponse)
async def embed(req: EmbedRequest, vr=Depends(get_router)) -> EmbedResponse:
    embeddings = await vr.embed(req.texts)
    return EmbedResponse(embeddings=embeddings)
