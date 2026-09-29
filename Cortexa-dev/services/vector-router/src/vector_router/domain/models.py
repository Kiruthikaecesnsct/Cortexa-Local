from enum import StrEnum

from pydantic import BaseModel


class VectorTarget(StrEnum):
    CORPUS = "corpus"
    ASSET = "asset"


class SearchFilter(BaseModel):
    field: str
    value: str


class VectorItem(BaseModel):
    id: str
    vector: list[float]
    payload: dict[str, str | int | float | bool]


class VectorHit(BaseModel):
    id: str
    score: float
    payload: dict[str, str | int | float | bool]
