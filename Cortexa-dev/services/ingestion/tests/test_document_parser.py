from unittest.mock import patch

import pytest

from ingestion.application.parsers.document_parser import parse_document
from ingestion.domain.enums.document_format import DocumentFormat
from ingestion.domain.errors.parser_errors import UnsupportedFormatError
from ingestion.domain.models.parsed_document import ParsedDocument


async def test_pdf_format_routes_to_pdf_parser(sample_pdf_bytes: bytes):
    mock_doc = ParsedDocument(text="pdf text", pages=[])
    with patch(
        "ingestion.application.parsers.document_parser.parse_pdf", return_value=mock_doc
    ) as mock_parse:
        result = await parse_document(sample_pdf_bytes, DocumentFormat.PDF)

    mock_parse.assert_called_once_with(sample_pdf_bytes)
    assert result == mock_doc


async def test_docx_format_routes_to_docx_parser(sample_docx_bytes: bytes):
    mock_doc = ParsedDocument(text="docx text", pages=[])
    with patch(
        "ingestion.application.parsers.document_parser.parse_docx", return_value=mock_doc
    ) as mock_parse:
        result = await parse_document(sample_docx_bytes, DocumentFormat.DOCX)

    mock_parse.assert_called_once_with(sample_docx_bytes)
    assert result == mock_doc


async def test_unsupported_format_raises():
    with pytest.raises(UnsupportedFormatError):
        await parse_document(b"data", "xml")  # type: ignore[arg-type]
