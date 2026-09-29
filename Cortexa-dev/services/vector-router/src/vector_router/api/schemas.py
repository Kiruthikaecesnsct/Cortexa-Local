from pydantic import BaseModel, Field

from ..domain.models import SearchFilter, VectorHit, VectorItem, VectorTarget


class SearchRequest(BaseModel):
    embedding: list[float] = Field(min_length=1)
    top_k: int = Field(default=10, ge=1, le=100)
    filters: list[SearchFilter] | None = None
    target: VectorTarget = VectorTarget.CORPUS


class SearchResponse(BaseModel):
    hits: list[VectorHit]


class UpsertRequest(BaseModel):
    items: list[VectorItem] = Field(min_length=1)
    target: VectorTarget = VectorTarget.CORPUS


class UpsertResponse(BaseModel):
    upserted: int


class AssetDeleteResponse(BaseModel):
    deleted: int


class EmbedRequest(BaseModel):
    texts: list[str] = Field(min_length=1)


class EmbedResponse(BaseModel):
    embeddings: list[list[float]]
