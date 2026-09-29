from typing import Literal

from pydantic import BaseModel


class AxisScoreDto(BaseModel):
    axis: str
    score: float
    reasoning: str
    citations: list[str]


class EvidenceSourceDto(BaseModel):
    source_type: str
    available: bool
    confidence: float
    citations: list[str]
    summary: str


class ProvenanceDto(BaseModel):
    source_document_id: str
    source_filename: str | None
    page_number: int | None
    span_start: int | None
    span_end: int | None
    excerpt_text: str


class SimilarPatentsDto(BaseModel):
    count: int
    source_count: int


class ClaimSeedsDto(BaseModel):
    count: int
    total: int


class CommercialPotentialDto(BaseModel):
    level: Literal["high", "medium", "low"]
    industry: str


class OpportunityDetailDto(BaseModel):
    candidate_id: str
    title: str
    abstract: str
    claim_draft: str
    overall_score: float
    recommendation: Literal["pursue", "investigate", "abandon"]
    axis_scores: list[AxisScoreDto]
    evidence_sources: list[EvidenceSourceDto]
    provenance: ProvenanceDto
    similar_patents: SimilarPatentsDto | None = None
    claim_seeds: ClaimSeedsDto | None = None
    commercial_potential: CommercialPotentialDto | None = None
