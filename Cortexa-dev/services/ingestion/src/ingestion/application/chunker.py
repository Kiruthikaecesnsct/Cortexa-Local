from ingestion.application import token_counter
from ingestion.application.geometry.word_mapper import GeometryContext, map_chunk_geometry
from ingestion.application.heading_detector import Heading, find_heading_for_position
from ingestion.domain.models.chunk import Chunk
from ingestion.domain.models.parsed_document import PageSpan


def _page_for_offset(offset: int, pages: list[PageSpan]) -> int | None:
    for page in pages:
        if page.start_char <= offset < page.end_char:
            return page.page_number
    return None


def _attach_geometry(chunk: Chunk, geometry: GeometryContext | None) -> Chunk:
    if geometry is None:
        return chunk
    rects, page_dimensions = map_chunk_geometry(chunk.start_char, chunk.end_char, geometry)
    if not rects:
        return chunk
    return chunk.model_copy(update={"chunk_rects": rects, "page_dimensions": page_dimensions})


def _build_chunk(
    tokens: list[int],
    window_start: int,
    window_end: int,
    order_index: int,
    encoding_name: str,
    pages: list[PageSpan] | None,
    headings: list[Heading] | None,
) -> Chunk:
    window_tokens = tokens[window_start:window_end]
    prefix_text = token_counter.decode(tokens[:window_start], encoding_name)
    start_char = len(prefix_text)
    chunk_text_str = token_counter.decode(window_tokens, encoding_name)
    end_char = start_char + len(chunk_text_str)
    page_number = _page_for_offset(start_char, pages) if pages else None
    section_hint = find_heading_for_position(start_char, headings) if headings else None
    return Chunk(
        text=chunk_text_str,
        order_index=order_index,
        start_char=start_char,
        end_char=end_char,
        token_count=len(window_tokens),
        page_number=page_number,
        section_hint=section_hint,
    )


def chunk_text(
    text: str,
    *,
    size: int = 512,
    overlap: int = 50,
    encoding_name: str = "cl100k_base",
    pages: list[PageSpan] | None = None,
    headings: list[Heading] | None = None,
    geometry: GeometryContext | None = None,
) -> list[Chunk]:
    if overlap >= size:
        raise ValueError(f"overlap ({overlap}) must be less than size ({size})")

    if not text or not text.strip():
        return []

    tokens = token_counter.encode(text, encoding_name)
    total_tokens = len(tokens)

    if total_tokens == 0:
        return []

    stride = size - overlap
    chunks: list[Chunk] = []
    order_index = 0
    window_start = 0

    while window_start < total_tokens:
        window_end = min(window_start + size, total_tokens)
        chunk = _build_chunk(
            tokens, window_start, window_end, order_index, encoding_name, pages, headings
        )
        chunks.append(_attach_geometry(chunk, geometry))
        order_index += 1
        if window_end == total_tokens:
            break
        window_start += stride

    return chunks
