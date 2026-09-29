from ingestion.application.text_normalizer import normalize_text


def test_collapses_multiple_spaces():
    result = normalize_text("hello   world")
    assert result == "hello world"


def test_normalizes_crlf_line_endings():
    result = normalize_text("line one\r\nline two")
    assert result == "line one\nline two"


def test_normalizes_cr_line_endings():
    result = normalize_text("line one\rline two")
    assert result == "line one\nline two"


def test_strips_trailing_whitespace_per_line():
    result = normalize_text("hello   \nworld  ")
    assert result == "hello\nworld"


def test_collapses_three_or_more_blank_lines_to_two():
    result = normalize_text("para one\n\n\n\npara two")
    assert result == "para one\n\npara two"


def test_exactly_two_blank_lines_preserved():
    result = normalize_text("para one\n\npara two")
    assert result == "para one\n\npara two"


def test_empty_string_returns_empty():
    assert normalize_text("") == ""


def test_whitespace_only_returns_empty():
    assert normalize_text("   \n  \n  ") == ""


def test_strips_leading_and_trailing_whitespace():
    result = normalize_text("\n\nhello\n\n")
    assert result == "hello"
