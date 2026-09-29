import pytest
import tiktoken

from ingestion.application import token_counter
from ingestion.application.chunker import chunk_text
from ingestion.application.heading_detector import Heading

_ENCODING = "cl100k_base"
_SIZE = 512
_OVERLAP = 50
_STRIDE = _SIZE - _OVERLAP


def _make_text_of_n_tokens(n: int, encoding_name: str = _ENCODING) -> str:
    enc = tiktoken.get_encoding(encoding_name)
    cycle = [264, 374, 387, 656, 733, 912, 709, 459]
    seq = (cycle * ((n // len(cycle)) + 1))[:n]
    return enc.decode(seq)


def _expected_chunk_count(total_tokens: int, size: int, stride: int) -> int:
    count = 0
    i = 0
    while i < total_tokens:
        count += 1
        end = min(i + size, total_tokens)
        if end == total_tokens:
            break
        i += stride
    return count


def test_chunk_count_and_second_chunk_start():
    text = _make_text_of_n_tokens(2000)
    chunks = chunk_text(text, size=_SIZE, overlap=_OVERLAP, encoding_name=_ENCODING)

    expected_count = _expected_chunk_count(2000, _SIZE, _STRIDE)
    assert len(chunks) == expected_count

    enc = tiktoken.get_encoding(_ENCODING)
    all_tokens = token_counter.encode(text, _ENCODING)
    prefix_tokens = all_tokens[:_STRIDE]
    expected_start_char = len(enc.decode(prefix_tokens))
    assert chunks[1].start_char == expected_start_char


def test_overlap_between_consecutive_chunks():
    text = _make_text_of_n_tokens(2000)
    chunks = chunk_text(text, size=_SIZE, overlap=_OVERLAP, encoding_name=_ENCODING)

    tokens_chunk_0 = token_counter.encode(chunks[0].text, _ENCODING)
    tokens_chunk_1 = token_counter.encode(chunks[1].text, _ENCODING)

    assert tokens_chunk_0[-_OVERLAP:] == tokens_chunk_1[:_OVERLAP]


def test_sequential_order_index():
    text = _make_text_of_n_tokens(1500)
    chunks = chunk_text(text, size=_SIZE, overlap=_OVERLAP, encoding_name=_ENCODING)
    for i, chunk in enumerate(chunks):
        assert chunk.order_index == i


def test_empty_string_returns_empty_list():
    assert chunk_text("") == []


def test_whitespace_only_returns_empty_list():
    assert chunk_text("   \n\t  ") == []


def test_short_text_returns_single_chunk():
    text = "Hello"
    chunks = chunk_text(text, size=_SIZE, overlap=_OVERLAP, encoding_name=_ENCODING)
    assert len(chunks) == 1
    assert chunks[0].order_index == 0


def test_text_exactly_size_tokens_returns_single_chunk():
    text = _make_text_of_n_tokens(_SIZE)
    chunks = chunk_text(text, size=_SIZE, overlap=_OVERLAP, encoding_name=_ENCODING)
    assert len(chunks) == 1
    assert chunks[0].order_index == 0
    assert chunks[0].token_count == _SIZE


def test_cjk_and_emoji_no_crash():
    text = "日本語のテキスト。" * 10 + " 🚀🎉" * 5
    chunks = chunk_text(text, size=_SIZE, overlap=_OVERLAP, encoding_name=_ENCODING)
    assert isinstance(chunks, list)
    for chunk in chunks:
        independent_count = token_counter.count(chunk.text, _ENCODING)
        assert chunk.token_count == independent_count


def test_char_offsets_reconstruct_original():
    text = "The quick brown fox. " * 100
    chunks = chunk_text(text, size=64, overlap=8, encoding_name=_ENCODING)
    for chunk in chunks:
        assert text[chunk.start_char : chunk.end_char] == chunk.text


def test_overlap_equal_to_size_raises_value_error():
    with pytest.raises(ValueError, match="overlap"):
        chunk_text("some text", size=10, overlap=10)


def test_chunk_source_with_special_token_literal_no_crash():
    # GPT training code (e.g. nanoGPT) contains "<|endoftext|>" verbatim; the
    # chunker must tokenize it as ordinary text rather than raising.
    text = "enc.encode('<|endoftext|>')\n" * 40
    chunks = chunk_text(text, size=_SIZE, overlap=_OVERLAP, encoding_name=_ENCODING)
    assert len(chunks) >= 1
    assert "".join(c.text for c in chunks[:1])


def test_section_hint_populated_from_headings():
    text = (
        "# Introduction\nContent about intro.\n## Methods\n"
        "Content about methods.\n## Results\nFinal content."
    )
    headings = [
        Heading("Introduction", 0, 15),
        Heading("Methods", 38, 48),
        Heading("Results", 72, 82),
    ]
    chunks = chunk_text(text, size=64, overlap=8, encoding_name=_ENCODING, headings=headings)

    assert len(chunks) >= 1
    assert chunks[0].section_hint == "Introduction"


def test_section_hint_none_when_no_headings():
    text = "Some content without any headings at all."
    chunks = chunk_text(text, size=64, overlap=8, encoding_name=_ENCODING)
    assert chunks[0].section_hint is None


def test_section_hint_none_when_headings_none():
    text = "Some content."
    chunks = chunk_text(text, size=64, overlap=8, encoding_name=_ENCODING, headings=None)
    assert chunks[0].section_hint is None


def test_section_hint_uses_nearest_preceding_heading():
    text = "A" * 1000
    headings = [
        Heading("First", 0, 10),
        Heading("Second", 500, 510),
    ]
    chunks = chunk_text(text, size=100, overlap=10, encoding_name=_ENCODING, headings=headings)

    first_chunk = chunks[0]
    assert first_chunk.section_hint == "First"

    later_chunks = [c for c in chunks if c.start_char >= 500]
    if later_chunks:
        assert later_chunks[0].section_hint == "Second"
