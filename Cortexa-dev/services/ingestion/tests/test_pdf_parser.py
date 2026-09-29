from unittest.mock import MagicMock, patch

import pytest

from ingestion.application.parsers.pdf_parser import parse_pdf
from ingestion.domain.errors.parser_errors import CorruptedFileError
from ingestion.domain.models.parsed_document import ParsedDocument


def test_parse_pdf_returns_nonempty_text(sample_pdf_bytes: bytes):
    result = parse_pdf(sample_pdf_bytes)
    assert isinstance(result, ParsedDocument)
    assert len(result.text) > 0
    assert isinstance(result.pages, list)


def test_parse_pdf_corrupted_bytes_raises(corrupted_bytes: bytes):
    with pytest.raises(CorruptedFileError, match="PDF parsing failed"):
        parse_pdf(corrupted_bytes)


def test_parse_pdf_page_with_none_text_treated_as_empty(sample_pdf_bytes: bytes):
    mock_page = MagicMock()
    mock_page.extract_text.return_value = None
    mock_page.extract_words.return_value = []
    mock_page.width = 0.0
    mock_page.height = 0.0

    mock_pdf = MagicMock()
    mock_pdf.__enter__ = MagicMock(return_value=mock_pdf)
    mock_pdf.__exit__ = MagicMock(return_value=False)
    mock_pdf.pages = [mock_page]

    with patch("ingestion.application.parsers.pdf_parser.pdfplumber.open", return_value=mock_pdf):
        result = parse_pdf(sample_pdf_bytes)

    assert result.text == ""
    assert result.pages == []
