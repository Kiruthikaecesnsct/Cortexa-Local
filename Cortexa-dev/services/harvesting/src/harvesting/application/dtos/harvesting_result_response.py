from typing import Literal

from pydantic import BaseModel, Field


class AxisScoreView(BaseModel):
    axis: str
    score: int
    refs: list[str]


class CitationView(BaseModel):
    ref: str
    source_type: str
    title: str
    patent_id: str | None = None
    url: str = ""
    similarity: float = 0.0


class EvidenceHitView(BaseModel):
    title: str = ""
    patent_id: str | None = None
    url: str = ""
    similarity: float = 0.0
    jurisdiction: str = ""


class EvidenceSourceView(BaseModel):
    source_type: str
    available: bool = False
    status: str = "empty"
    hits: list[EvidenceHitView] = Field(default_factory=list)
    confidence: float = 0.0
    reasoning: list[str] = Field(default_factory=list)
    degraded: bool = False
    degraded_sources: list[str] = Field(default_factory=list)


class PageDimensionView(BaseModel):
    page_number: int
    width: float
    height: float


class HighlightRectView(BaseModel):
    page_number: int
    x0: float
    x1: float
    top: float
    bottom: float


class LineRangeView(BaseModel):
    start_line: int
    end_line: int


class ProvenanceLinkView(BaseModel):
    document_id: str
    locator: str = ""
    source_kind: str = ""
    hit_url: str | None = None
    chunk_id: str | None = None
    source_chunk_index: int | None = None
    page_number: int | None = None
    section_hint: str | None = None
    span_start: int | None = None
    span_end: int | None = None
    excerpt: str | None = None
    preview_kind: Literal["pdf", "code", "none"] = "none"
    page_dimensions: list[PageDimensionView] | None = None
    highlight_rects: list[HighlightRectView] | None = None
    clean_excerpt: str | None = None
    file_path: str | None = None
    line_range: LineRangeView | None = None


class CandidateDto(BaseModel):
    id: str
    title: str
    abstract: str
    description: str
    claim_draft: str
    novelty_hypothesis: str
    source_asset_id: str
    batch_id: str
    created_at: str
    candidate_id: str
    maturity: str
    rank: int
    weighted_score: float
    axes: dict[str, AxisScoreView]
    agreement_flag: str
    citations: list[CitationView]
    provenance_links: list[ProvenanceLinkView]
    source_availability: dict[str, bool] = Field(default_factory=dict)
    source_status: dict[str, str] = Field(default_factory=dict)
    evidence_sources: list[EvidenceSourceView] = Field(default_factory=list)


class VerdictDto(BaseModel):
    id: str
    candidate_id: str
    patentability_score: float
    rationale: str
    recommendation: str
    batch_id: str
    created_at: str


class HarvestingResultResponseDto(BaseModel):
    id: str
    batch_id: str
    candidates: list[CandidateDto]
    verdicts: list[VerdictDto]
    summary: str
    created_at: str
