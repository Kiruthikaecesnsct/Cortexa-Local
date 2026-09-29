from ingestion.application.chunker import chunk_text
from ingestion.domain.models.parsed_document import PageSpan


def test_page_for_offset_maps_chunk_to_correct_page():
    text = "Page one text.\nPage two text.\nPage three text."
    pages = [
        PageSpan(page_number=1, start_char=0, end_char=15),
        PageSpan(page_number=2, start_char=16, end_char=31),
        PageSpan(page_number=3, start_char=32, end_char=50),
    ]

    chunks = chunk_text(text, size=10, overlap=2, pages=pages)

    assert len(chunks) > 0
    assert all(chunk.page_number is not None for chunk in chunks)
    assert chunks[0].page_number == 1


def test_chunk_without_pages_has_none_page_number():
    text = "Some text without page information"
    chunks = chunk_text(text, size=10, overlap=2, pages=None)

    assert len(chunks) > 0
    assert all(chunk.page_number is None for chunk in chunks)


def test_empty_pages_list_results_in_none_page_numbers():
    text = "Some text"
    chunks = chunk_text(text, size=10, overlap=2, pages=[])

    assert len(chunks) > 0
    assert all(chunk.page_number is None for chunk in chunks)


def test_chunk_spanning_page_boundary_gets_first_page():
    text = "First page.\nSecond page."
    pages = [
        PageSpan(page_number=1, start_char=0, end_char=12),
        PageSpan(page_number=2, start_char=13, end_char=25),
    ]

    chunks = chunk_text(text, size=100, overlap=0, pages=pages)

    assert len(chunks) == 1
    assert chunks[0].page_number == 1


def test_multiple_chunks_across_multiple_pages():
    page1_text = "Page one text. " * 20
    page2_text = "Page two text. " * 20
    page3_text = "Page three text. " * 20
    text = page1_text + "\n" + page2_text + "\n" + page3_text

    page1_len = len(page1_text)
    page2_len = len(page2_text)

    pages = [
        PageSpan(page_number=1, start_char=0, end_char=page1_len),
        PageSpan(page_number=2, start_char=page1_len + 1, end_char=page1_len + 1 + page2_len),
        PageSpan(page_number=3, start_char=page1_len + 1 + page2_len + 1, end_char=len(text)),
    ]

    chunks = chunk_text(text, size=50, overlap=10, pages=pages)

    page_numbers = [c.page_number for c in chunks]

    assert 1 in page_numbers
    assert 2 in page_numbers
    assert 3 in page_numbers
