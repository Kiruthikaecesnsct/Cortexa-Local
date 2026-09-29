from ingestion.application.geometry.word_mapper import build_geometry_context, map_chunk_geometry
from ingestion.domain.models.parsed_document import (
    PageDimensions,
    PageSpan,
    ParsedDocument,
    WordBox,
)

_PAGE_WIDTH = 612.0
_PAGE_HEIGHT = 792.0


def _word(page_number: int, text: str, x0: float, x1: float, top: float, bottom: float) -> WordBox:
    return WordBox(
        page_number=page_number,
        text=text,
        x0=x0 / _PAGE_WIDTH,
        x1=x1 / _PAGE_WIDTH,
        top=top / _PAGE_HEIGHT,
        bottom=bottom / _PAGE_HEIGHT,
    )


def _single_page_doc(text: str, words: list[WordBox]) -> ParsedDocument:
    return ParsedDocument(
        text=text,
        pages=[PageSpan(page_number=1, start_char=0, end_char=len(text))],
        page_dimensions=[PageDimensions(page_number=1, width=_PAGE_WIDTH, height=_PAGE_HEIGHT)],
        words=words,
    )


def test_build_geometry_context_aligns_words_to_global_offsets():
    words = [
        _word(1, "Hello", 100.0, 127.336, 82.484, 94.484),
        _word(1, "World", 134.008, 165.34, 82.484, 94.484),
    ]
    parsed = _single_page_doc("Hello World", words)

    geometry = build_geometry_context(parsed, max_words_per_page=100)

    assert len(geometry.mapped_words) == 2
    assert geometry.mapped_words[0].global_start == 0
    assert geometry.mapped_words[0].global_end == 5
    assert geometry.mapped_words[1].global_start == 6
    assert geometry.mapped_words[1].global_end == 11


def test_map_chunk_geometry_merges_same_line_words_into_one_rect():
    words = [
        _word(1, "Hello", 100.0, 127.336, 82.484, 94.484),
        _word(1, "World", 134.008, 165.34, 82.484, 94.484),
    ]
    parsed = _single_page_doc("Hello World", words)
    geometry = build_geometry_context(parsed, max_words_per_page=100)

    rects, page_dims = map_chunk_geometry(0, 11, geometry)

    assert len(rects) == 1
    rect = rects[0]
    assert rect.page_number == 1
    assert rect.x0 == words[0].x0
    assert rect.x1 == words[1].x1
    assert len(page_dims) == 1
    assert page_dims[0].page_number == 1


def test_map_chunk_geometry_rect_coordinates_within_0_and_1():
    words = [_word(1, "Hello", 100.0, 127.336, 82.484, 94.484)]
    parsed = _single_page_doc("Hello", words)
    geometry = build_geometry_context(parsed, max_words_per_page=100)

    rects, _ = map_chunk_geometry(0, 5, geometry)

    assert len(rects) == 1
    rect = rects[0]
    for value in (rect.x0, rect.x1, rect.top, rect.bottom):
        assert 0.0 <= value <= 1.0


def test_map_chunk_geometry_partial_span_selects_only_covering_word():
    words = [
        _word(1, "Hello", 100.0, 127.336, 82.484, 94.484),
        _word(1, "World", 134.008, 165.34, 82.484, 94.484),
    ]
    parsed = _single_page_doc("Hello World", words)
    geometry = build_geometry_context(parsed, max_words_per_page=100)

    rects, _ = map_chunk_geometry(0, 5, geometry)

    assert len(rects) == 1
    assert rects[0].x1 == words[0].x1


def test_map_chunk_geometry_words_on_different_lines_produce_separate_rects():
    words = [
        _word(1, "Hello", 100.0, 127.336, 82.484, 94.484),
        _word(1, "World", 100.0, 127.336, 200.0, 212.0),
    ]
    parsed = _single_page_doc("Hello World", words)
    geometry = build_geometry_context(parsed, max_words_per_page=100)

    rects, _ = map_chunk_geometry(0, 11, geometry)

    assert len(rects) == 2


def test_map_chunk_geometry_groups_multi_page_rects_and_returns_matching_dimensions():
    page1_words = [_word(1, "First", 100.0, 130.0, 82.484, 94.484)]
    page2_words = [_word(2, "Second", 100.0, 140.0, 82.484, 94.484)]
    text = "First\nSecond"
    parsed = ParsedDocument(
        text=text,
        pages=[
            PageSpan(page_number=1, start_char=0, end_char=5),
            PageSpan(page_number=2, start_char=6, end_char=12),
        ],
        page_dimensions=[
            PageDimensions(page_number=1, width=_PAGE_WIDTH, height=_PAGE_HEIGHT),
            PageDimensions(page_number=2, width=_PAGE_WIDTH, height=_PAGE_HEIGHT),
        ],
        words=page1_words + page2_words,
    )
    geometry = build_geometry_context(parsed, max_words_per_page=100)

    rects, page_dims = map_chunk_geometry(0, 12, geometry)

    assert {r.page_number for r in rects} == {1, 2}
    assert {pd.page_number for pd in page_dims} == {1, 2}


def test_build_geometry_context_respects_max_words_per_page_cap():
    words = [
        _word(1, "Alpha", 100.0, 130.0, 82.484, 94.484),
        _word(1, "Beta", 134.0, 160.0, 82.484, 94.484),
        _word(1, "Gamma", 164.0, 190.0, 82.484, 94.484),
    ]
    parsed = _single_page_doc("Alpha Beta Gamma", words)

    geometry = build_geometry_context(parsed, max_words_per_page=1)

    assert len(geometry.mapped_words) == 1
    assert geometry.mapped_words[0].global_start == 0


def test_build_geometry_context_empty_words_returns_empty_mapped_words():
    parsed = _single_page_doc("No words here", [])

    geometry = build_geometry_context(parsed, max_words_per_page=100)

    assert geometry.mapped_words == ()

    rects, page_dims = map_chunk_geometry(0, 5, geometry)
    assert rects == []
    assert page_dims == []


def test_build_geometry_context_skips_word_not_found_in_page_text():
    words = [
        _word(1, "Hello", 100.0, 127.336, 82.484, 94.484),
        _word(1, "Ghost", 134.008, 165.34, 82.484, 94.484),
    ]
    parsed = _single_page_doc("Hello World", words)

    geometry = build_geometry_context(parsed, max_words_per_page=100)

    assert len(geometry.mapped_words) == 1
    assert geometry.mapped_words[0].global_start == 0
