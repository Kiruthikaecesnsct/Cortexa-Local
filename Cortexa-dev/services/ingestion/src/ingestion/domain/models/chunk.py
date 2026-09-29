from pydantic import BaseModel

from ingestion.domain.models.parsed_document import PageDimensions


class ChunkRect(BaseModel):
    page_number: int
    x0: float
    x1: float
    top: float
    bottom: float


class Chunk(BaseModel):
    text: str
    order_index: int
    start_char: int
    end_char: int
    token_count: int
    page_number: int | None = None
    section_hint: str | None = None
    page_dimensions: list[PageDimensions] | None = None
    chunk_rects: list[ChunkRect] | None = None
