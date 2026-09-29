from typing import Literal

from pydantic import BaseModel

# Evidence-backed citation and provenance link value objects for harvesting reports.
#
# These mirror the payloads that the scoring service stores in the Cosmos
# `evidence_bundles` container (hit fields) and the shape of scoring's
# `ProvenanceDto`. Harvesting resolves them from a raw evidence-bundle dict
# rather than importing scoring code (services share no libraries).
#
# Field names/types match the evidence-bundle `hits[]` entries produced by
# scoring's `EvidenceHit` model
# (services/scoring/src/scoring/domain/models/evidence_hit.py).
#
# PageDimension/HighlightRect mirror ingestion's PageDimensions/ChunkRect
# (services/ingestion/src/ingestion/domain/models/parsed_document.py and
# chunk.py), captured at ingestion time by US122's geometry mapper.


class Citation(BaseModel):
    """A single evidence reference (e.g. "E6") resolved to its underlying hit."""

    ref: str
    source_type: str
    title: str
    patent_id: str | None = None
    url: str = ""
    similarity: float = 0.0


class PageDimension(BaseModel):
    page_number: int
    width: float
    height: float


class HighlightRect(BaseModel):
    page_number: int
    x0: float
    x1: float
    top: float
    bottom: float


class LineRange(BaseModel):
    start_line: int
    end_line: int


class ProvenanceLink(BaseModel):
    """Where a candidate was mined from — mirrors scoring's ProvenanceDto inputs."""

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
    page_dimensions: list[PageDimension] | None = None
    highlight_rects: list[HighlightRect] | None = None
    clean_excerpt: str | None = None
    file_path: str | None = None
    line_range: LineRange | None = None
