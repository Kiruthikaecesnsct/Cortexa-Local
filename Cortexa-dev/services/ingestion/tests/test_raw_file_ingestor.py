from unittest.mock import AsyncMock, patch

import pytest

from ingestion.application.parsers.pdf_parser import parse_pdf
from ingestion.application.raw_file_ingestor import IngestorConfig, assemble_ingest_request
from ingestion.domain.enums.source_kind import SourceKind
from ingestion.domain.errors.parser_errors import (
    ConversionError,
    ConversionTimeoutError,
    UnsupportedFormatError,
)
from ingestion.domain.models.parsed_document import PageDimensions, PageSpan, ParsedDocument

BATCH_ID = "batch-001"
DOCUMENT_ID = "doc-001"
CORRELATION_ID = "corr-001"

_CONFIG = IngestorConfig(chunk_size=512, chunk_overlap=50, chunk_encoding="cl100k_base")
_CONVERTER_TARGET = "ingestion.application.raw_file_ingestor.convert_docx_to_pdf"


async def _assemble(raw_content: bytes, filename: str, content_type: str):
    return await assemble_ingest_request(
        batch_id=BATCH_ID,
        document_id=DOCUMENT_ID,
        filename=filename,
        content_type=content_type,
        raw_content=raw_content,
        correlation_id=CORRELATION_ID,
        config=_CONFIG,
    )


async def test_assemble_from_pdf_bytes_returns_paper_source_kind(sample_pdf_bytes):
    request = await assemble_ingest_request(
        batch_id=BATCH_ID,
        document_id=DOCUMENT_ID,
        filename="paper.pdf",
        content_type="application/pdf",
        raw_content=sample_pdf_bytes,
        correlation_id=CORRELATION_ID,
        config=_CONFIG,
    )

    assert request.source_kind == SourceKind.PAPER


async def test_assemble_from_pdf_bytes_sets_ids(sample_pdf_bytes):
    request = await assemble_ingest_request(
        batch_id=BATCH_ID,
        document_id=DOCUMENT_ID,
        filename="paper.pdf",
        content_type="application/pdf",
        raw_content=sample_pdf_bytes,
        correlation_id=CORRELATION_ID,
        config=_CONFIG,
    )

    assert request.batch_id == BATCH_ID
    assert request.document_id == DOCUMENT_ID
    assert request.correlation_id == CORRELATION_ID


async def test_assemble_from_pdf_bytes_produces_chunks(sample_pdf_bytes):
    request = await assemble_ingest_request(
        batch_id=BATCH_ID,
        document_id=DOCUMENT_ID,
        filename="paper.pdf",
        content_type="application/pdf",
        raw_content=sample_pdf_bytes,
        correlation_id=CORRELATION_ID,
        config=_CONFIG,
    )

    assert len(request.chunks) > 0
    assert len(request.provenance_map.entries) == len(request.chunks)


async def test_assemble_from_pdf_bytes_preserves_raw_content(sample_pdf_bytes):
    request = await assemble_ingest_request(
        batch_id=BATCH_ID,
        document_id=DOCUMENT_ID,
        filename="paper.pdf",
        content_type="application/pdf",
        raw_content=sample_pdf_bytes,
        correlation_id=CORRELATION_ID,
        config=_CONFIG,
    )

    assert request.raw_content == sample_pdf_bytes


async def test_assemble_from_docx_bytes_succeeds(sample_docx_bytes):
    request = await assemble_ingest_request(
        batch_id=BATCH_ID,
        document_id=DOCUMENT_ID,
        filename="paper.docx",
        content_type="application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        raw_content=sample_docx_bytes,
        correlation_id=CORRELATION_ID,
        config=_CONFIG,
    )

    assert request.source_kind == SourceKind.PAPER
    assert len(request.chunks) > 0


async def test_assemble_resolves_format_from_filename_extension_when_no_content_type(
    sample_pdf_bytes,
):
    request = await assemble_ingest_request(
        batch_id=BATCH_ID,
        document_id=DOCUMENT_ID,
        filename="paper.pdf",
        content_type="",
        raw_content=sample_pdf_bytes,
        correlation_id=CORRELATION_ID,
        config=_CONFIG,
    )

    assert request.source_kind == SourceKind.PAPER


async def test_assemble_content_type_takes_priority_over_extension(sample_pdf_bytes):
    request = await assemble_ingest_request(
        batch_id=BATCH_ID,
        document_id=DOCUMENT_ID,
        filename="paper.docx",
        content_type="application/pdf",
        raw_content=sample_pdf_bytes,
        correlation_id=CORRELATION_ID,
        config=_CONFIG,
    )

    assert request.source_kind == SourceKind.PAPER


async def test_assemble_raises_unsupported_format_for_unknown_type():
    with pytest.raises(UnsupportedFormatError):
        await assemble_ingest_request(
            batch_id=BATCH_ID,
            document_id=DOCUMENT_ID,
            filename="data.csv",
            content_type="text/csv",
            raw_content=b"col1,col2\n1,2",
            correlation_id=CORRELATION_ID,
            config=_CONFIG,
        )


async def test_assemble_provenance_entries_reference_document_id(sample_pdf_bytes):
    request = await assemble_ingest_request(
        batch_id=BATCH_ID,
        document_id=DOCUMENT_ID,
        filename="paper.pdf",
        content_type="application/pdf",
        raw_content=sample_pdf_bytes,
        correlation_id=CORRELATION_ID,
        config=_CONFIG,
    )

    for entry in request.provenance_map.entries:
        assert entry.doc_id == DOCUMENT_ID


async def test_pdf_upload_gets_viewable_pdf_equal_to_raw_bytes(two_page_pdf_bytes):
    request = await _assemble(two_page_pdf_bytes, "paper.pdf", "application/pdf")

    assert request.viewable_pdf == two_page_pdf_bytes


async def test_pdf_upload_geometry_reason_none_when_words_present(two_page_pdf_bytes):
    request = await _assemble(two_page_pdf_bytes, "paper.pdf", "application/pdf")

    assert request.geometry_reason is None
    assert any(c.chunk_rects for c in request.chunks)


async def test_pdf_char_spans_are_invariant_regardless_of_geometry_capture(two_page_pdf_bytes):
    request = await _assemble(two_page_pdf_bytes, "paper.pdf", "application/pdf")
    parsed = parse_pdf(two_page_pdf_bytes)

    for chunk in request.chunks:
        assert parsed.text[chunk.start_char : chunk.end_char] == chunk.text


async def test_scanned_pdf_with_no_words_sets_geometry_unavailable_reason(sample_pdf_bytes):
    scanned_doc = ParsedDocument(
        text="Some OCR-less text with a text layer but no positional word data.",
        pages=[PageSpan(page_number=1, start_char=0, end_char=67)],
        page_dimensions=[PageDimensions(page_number=1, width=612.0, height=792.0)],
        words=[],
    )
    with patch("ingestion.application.raw_file_ingestor.parse_pdf", return_value=scanned_doc):
        request = await _assemble(sample_pdf_bytes, "scanned.pdf", "application/pdf")

    assert request.geometry_reason == "no_extractable_words"
    for chunk in request.chunks:
        assert chunk.chunk_rects is None


async def test_docx_conversion_success_sets_viewable_pdf_and_page_number(sample_docx_bytes):
    fake_pdf = _fake_converted_pdf()
    with patch(_CONVERTER_TARGET, new=AsyncMock(return_value=fake_pdf)):
        request = await _assemble(
            sample_docx_bytes,
            "paper.docx",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        )

    assert request.viewable_pdf == fake_pdf
    assert request.geometry_reason is None
    assert all(chunk.page_number is not None for chunk in request.chunks)


async def test_docx_conversion_timeout_degrades_gracefully(sample_docx_bytes):
    with patch(_CONVERTER_TARGET, new=AsyncMock(side_effect=ConversionTimeoutError("too slow"))):
        request = await _assemble(
            sample_docx_bytes,
            "paper.docx",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        )

    assert request.viewable_pdf is None
    assert request.geometry_reason == "conversion_timeout"
    assert len(request.chunks) > 0
    for chunk in request.chunks:
        assert chunk.chunk_rects is None


async def test_docx_conversion_failure_degrades_gracefully(sample_docx_bytes):
    with patch(_CONVERTER_TARGET, new=AsyncMock(side_effect=ConversionError("soffice exploded"))):
        request = await _assemble(
            sample_docx_bytes,
            "paper.docx",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        )

    assert request.viewable_pdf is None
    assert request.geometry_reason == "conversion_failed"
    assert len(request.chunks) > 0


async def test_geometry_mapping_exception_degrades_to_text_only_provenance(two_page_pdf_bytes):
    with patch(
        "ingestion.application.raw_file_ingestor.build_geometry_context",
        side_effect=RuntimeError("boom"),
    ):
        request = await _assemble(two_page_pdf_bytes, "paper.pdf", "application/pdf")

    assert request.geometry_reason == "geometry_unavailable: mapping_error"
    assert len(request.chunks) > 0
    for chunk in request.chunks:
        assert chunk.chunk_rects is None
        assert chunk.page_dimensions is None


async def test_geometry_disabled_config_skips_word_mapping(two_page_pdf_bytes):
    config = IngestorConfig(
        chunk_size=512, chunk_overlap=50, chunk_encoding="cl100k_base", geometry_enabled=False
    )
    request = await assemble_ingest_request(
        batch_id=BATCH_ID,
        document_id=DOCUMENT_ID,
        filename="paper.pdf",
        content_type="application/pdf",
        raw_content=two_page_pdf_bytes,
        correlation_id=CORRELATION_ID,
        config=config,
    )

    assert request.geometry_reason == "geometry_disabled"
    for chunk in request.chunks:
        assert chunk.chunk_rects is None


def _fake_converted_pdf() -> bytes:
    from tests.conftest import make_multi_page_pdf

    return make_multi_page_pdf(["Converted page one", "Converted page two"])
