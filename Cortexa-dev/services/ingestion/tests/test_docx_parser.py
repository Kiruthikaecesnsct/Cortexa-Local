import io

import docx
import pytest

from ingestion.application.parsers.docx_parser import parse_docx
from ingestion.domain.errors.parser_errors import CorruptedFileError
from ingestion.domain.models.parsed_document import ParsedDocument


def test_parse_docx_returns_paragraph_text(sample_docx_bytes: bytes):
    result = parse_docx(sample_docx_bytes)
    assert isinstance(result, ParsedDocument)
    assert "First paragraph" in result.text
    assert "Second paragraph" in result.text
    assert result.pages == []


def test_parse_docx_corrupted_bytes_raises(corrupted_bytes: bytes):
    with pytest.raises(CorruptedFileError, match="DOCX parsing failed"):
        parse_docx(corrupted_bytes)


def test_parse_docx_empty_document_returns_empty():
    doc = docx.Document()
    buf = io.BytesIO()
    doc.save(buf)
    result = parse_docx(buf.getvalue())
    assert result.text == ""
    assert result.pages == []
