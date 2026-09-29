from ingestion.application.provenance.line_mapper import char_range_to_line_range


def test_single_line_full_span() -> None:
    text = "hello world"
    assert char_range_to_line_range(text, 0, len(text)) == (1, 1)


def test_chunk_on_line_two() -> None:
    text = "line one\nline two\nline three"
    start = text.index("line two")
    end = start + len("line two")
    assert char_range_to_line_range(text, start, end) == (2, 2)


def test_chunk_spanning_lines_three_to_five() -> None:
    lines = ["line1\n", "line2\n", "line3\n", "line4\n", "line5\n", "line6\n"]
    text = "".join(lines)
    start = sum(len(line) for line in lines[:2])
    end = sum(len(line) for line in lines[:5])
    assert char_range_to_line_range(text, start, end) == (3, 5)


def test_chunk_starting_at_beginning() -> None:
    text = "a\nb\nc\nd"
    assert char_range_to_line_range(text, 0, len(text)) == (1, 4)


def test_chunk_ending_exactly_on_newline() -> None:
    text = "line1\nline2\n"
    assert char_range_to_line_range(text, 0, 6) == (1, 1)


def test_empty_text_edge_case() -> None:
    assert char_range_to_line_range("", 0, 0) == (1, 1)
