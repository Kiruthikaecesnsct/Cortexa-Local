from dataclasses import dataclass

from ingestion.domain.models.chunk import ChunkRect
from ingestion.domain.models.parsed_document import PageDimensions, ParsedDocument

_LINE_TOLERANCE = 0.015


@dataclass(frozen=True)
class _MappedWord:
    page_number: int
    global_start: int
    global_end: int
    x0: float
    x1: float
    top: float
    bottom: float


@dataclass(frozen=True)
class GeometryContext:
    mapped_words: tuple[_MappedWord, ...]
    page_dimensions: tuple[PageDimensions, ...]


def _align_page_words(
    words: list, page_text: str, page_start_char: int, max_words: int
) -> list[_MappedWord]:
    mapped: list[_MappedWord] = []
    cursor = 0
    for word in words[:max_words]:
        token = word.text.strip()
        if not token:
            continue
        idx = page_text.find(token, cursor)
        if idx == -1:
            continue
        start = page_start_char + idx
        end = start + len(token)
        mapped.append(
            _MappedWord(
                page_number=word.page_number,
                global_start=start,
                global_end=end,
                x0=word.x0,
                x1=word.x1,
                top=word.top,
                bottom=word.bottom,
            )
        )
        cursor = idx + len(token)
    return mapped


def build_geometry_context(parsed: ParsedDocument, max_words_per_page: int) -> GeometryContext:
    words_by_page: dict[int, list] = {}
    for word in parsed.words:
        words_by_page.setdefault(word.page_number, []).append(word)

    mapped_words: list[_MappedWord] = []
    for page in parsed.pages:
        page_words = words_by_page.get(page.page_number, [])
        if not page_words:
            continue
        page_text = parsed.text[page.start_char : page.end_char]
        mapped_words.extend(
            _align_page_words(page_words, page_text, page.start_char, max_words_per_page)
        )

    return GeometryContext(
        mapped_words=tuple(mapped_words),
        page_dimensions=tuple(parsed.page_dimensions),
    )


def _words_overlapping_span(
    geometry: GeometryContext, start_char: int, end_char: int
) -> list[_MappedWord]:
    return [
        w for w in geometry.mapped_words if w.global_start < end_char and w.global_end > start_char
    ]


def _same_line(a: _MappedWord, b: _MappedWord) -> bool:
    return abs(a.top - b.top) <= _LINE_TOLERANCE


def _group_into_lines(words: list[_MappedWord]) -> list[list[_MappedWord]]:
    lines: list[list[_MappedWord]] = []
    for word in sorted(words, key=lambda w: (w.page_number, w.top, w.x0)):
        target = next(
            (
                line
                for line in lines
                if line[-1].page_number == word.page_number and _same_line(line[-1], word)
            ),
            None,
        )
        if target is not None:
            target.append(word)
        else:
            lines.append([word])
    return lines


def _rect_from_line(line: list[_MappedWord]) -> ChunkRect:
    return ChunkRect(
        page_number=line[0].page_number,
        x0=min(w.x0 for w in line),
        x1=max(w.x1 for w in line),
        top=min(w.top for w in line),
        bottom=max(w.bottom for w in line),
    )


def _page_dimensions_for(geometry: GeometryContext, page_numbers: set[int]) -> list[PageDimensions]:
    return sorted(
        (pd for pd in geometry.page_dimensions if pd.page_number in page_numbers),
        key=lambda pd: pd.page_number,
    )


def map_chunk_geometry(
    start_char: int, end_char: int, geometry: GeometryContext
) -> tuple[list[ChunkRect], list[PageDimensions]]:
    overlapping = _words_overlapping_span(geometry, start_char, end_char)
    if not overlapping:
        return [], []

    lines = _group_into_lines(overlapping)
    rects = [_rect_from_line(line) for line in lines]
    page_numbers = {rect.page_number for rect in rects}
    return rects, _page_dimensions_for(geometry, page_numbers)
