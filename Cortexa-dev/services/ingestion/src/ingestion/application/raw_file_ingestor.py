import asyncio
import logging
from dataclasses import dataclass
from pathlib import Path

from ingestion.application.chunker import chunk_text
from ingestion.application.dtos.ingest_request import IngestRequest
from ingestion.application.geometry.word_mapper import build_geometry_context
from ingestion.application.heading_detector import Heading, detect_headings
from ingestion.application.parsers.docx_converter import convert_docx_to_pdf
from ingestion.application.parsers.docx_parser import parse_docx
from ingestion.application.parsers.pdf_parser import parse_pdf
from ingestion.application.provenance.provenance_builder import (
    build_paper_entries,
    build_provenance_map,
)
from ingestion.domain.enums.document_format import DocumentFormat
from ingestion.domain.enums.source_kind import SourceKind
from ingestion.domain.errors.parser_errors import (
    ConversionError,
    ConversionTimeoutError,
    CorruptedFileError,
    UnsupportedFormatError,
)
from ingestion.domain.models.chunk import Chunk
from ingestion.domain.models.parsed_document import ParsedDocument

logger = logging.getLogger(__name__)

_CONTENT_TYPE_MAP: dict[str, DocumentFormat] = {
    "application/pdf": DocumentFormat.PDF,
    "application/vnd.openxmlformats-officedocument.wordprocessingml.document": DocumentFormat.DOCX,
}

_EXTENSION_MAP: dict[str, DocumentFormat] = {
    ".pdf": DocumentFormat.PDF,
    ".docx": DocumentFormat.DOCX,
}

_REASON_NO_WORDS = "no_extractable_words"
_REASON_CONVERSION_FAILED = "conversion_failed"
_REASON_CONVERSION_TIMEOUT = "conversion_timeout"
_REASON_CONVERTED_PDF_UNPARSEABLE = "converted_pdf_unparseable"
_REASON_GEOMETRY_DISABLED = "geometry_disabled"
_REASON_MAPPING_ERROR = "geometry_unavailable: mapping_error"


@dataclass(frozen=True)
class IngestorConfig:
    chunk_size: int
    chunk_overlap: int
    chunk_encoding: str
    docx_convert_timeout_seconds: float = 60.0
    geometry_enabled: bool = True
    geometry_max_words_per_page: int = 3000


@dataclass(frozen=True)
class _CapturedAsset:
    parsed: ParsedDocument
    viewable_pdf: bytes | None
    geometry_reason: str | None


@dataclass(frozen=True)
class _RequestIds:
    batch_id: str
    document_id: str


def _resolve_format(content_type: str, filename: str) -> DocumentFormat:
    fmt = _CONTENT_TYPE_MAP.get(content_type)
    if fmt is not None:
        return fmt
    ext = Path(filename).suffix.lower()
    fmt = _EXTENSION_MAP.get(ext)
    if fmt is not None:
        return fmt
    raise UnsupportedFormatError(
        f"Cannot determine format from content-type '{content_type}' or filename '{filename}'"
    )


def _geometry_reason_for(parsed: ParsedDocument) -> str | None:
    if parsed.pages and not parsed.words:
        return _REASON_NO_WORDS
    return None


async def _capture_pdf(raw_content: bytes) -> _CapturedAsset:
    parsed = await asyncio.to_thread(parse_pdf, raw_content)
    return _CapturedAsset(
        parsed=parsed, viewable_pdf=raw_content, geometry_reason=_geometry_reason_for(parsed)
    )


async def _docx_text_only_fallback(raw_content: bytes, reason: str) -> _CapturedAsset:
    parsed = await asyncio.to_thread(parse_docx, raw_content)
    return _CapturedAsset(parsed=parsed, viewable_pdf=None, geometry_reason=reason)


async def _capture_docx(raw_content: bytes, config: IngestorConfig) -> _CapturedAsset:
    try:
        pdf_bytes = await convert_docx_to_pdf(raw_content, config.docx_convert_timeout_seconds)
    except ConversionTimeoutError:
        return await _docx_text_only_fallback(raw_content, _REASON_CONVERSION_TIMEOUT)
    except ConversionError:
        return await _docx_text_only_fallback(raw_content, _REASON_CONVERSION_FAILED)

    try:
        parsed = await asyncio.to_thread(parse_pdf, pdf_bytes)
    except CorruptedFileError:
        return await _docx_text_only_fallback(raw_content, _REASON_CONVERTED_PDF_UNPARSEABLE)

    return _CapturedAsset(
        parsed=parsed, viewable_pdf=pdf_bytes, geometry_reason=_geometry_reason_for(parsed)
    )


async def _capture_asset(
    fmt: DocumentFormat, raw_content: bytes, config: IngestorConfig
) -> _CapturedAsset:
    if fmt == DocumentFormat.PDF:
        return await _capture_pdf(raw_content)
    return await _capture_docx(raw_content, config)


def _log_geometry_outcome(ids: _RequestIds, captured: _CapturedAsset) -> None:
    if captured.geometry_reason is None:
        return
    logger.info(
        "batch_id=%s document_id=%s geometry_reason=%s page_count=%d word_count=%d",
        ids.batch_id,
        ids.document_id,
        captured.geometry_reason,
        len(captured.parsed.pages),
        len(captured.parsed.words),
    )


def _log_mapping_error(ids: _RequestIds, parsed: ParsedDocument, exc: Exception) -> None:
    logger.warning(
        "batch_id=%s document_id=%s geometry_reason=%s page_count=%d word_count=%d error_type=%s",
        ids.batch_id,
        ids.document_id,
        _REASON_MAPPING_ERROR,
        len(parsed.pages),
        len(parsed.words),
        type(exc).__name__,
    )


def _resolved_geometry_reason(captured: _CapturedAsset, config: IngestorConfig) -> str | None:
    if captured.geometry_reason is not None:
        return captured.geometry_reason
    if not config.geometry_enabled:
        return _REASON_GEOMETRY_DISABLED
    return None


def _run_chunk_text(
    parsed: ParsedDocument, headings: list[Heading] | None, config: IngestorConfig, geometry
) -> list[Chunk]:
    return chunk_text(
        parsed.text,
        size=config.chunk_size,
        overlap=config.chunk_overlap,
        encoding_name=config.chunk_encoding,
        pages=parsed.pages if parsed.pages else None,
        headings=headings if headings else None,
        geometry=geometry,
    )


def _chunk_with_geometry(
    ids: _RequestIds,
    headings: list[Heading] | None,
    config: IngestorConfig,
    captured: _CapturedAsset,
) -> tuple[list[Chunk], str | None]:
    parsed = captured.parsed
    reason = _resolved_geometry_reason(captured, config)

    if not config.geometry_enabled or not parsed.words:
        return _run_chunk_text(parsed, headings, config, None), reason

    try:
        geometry = build_geometry_context(parsed, config.geometry_max_words_per_page)
        chunks = _run_chunk_text(parsed, headings, config, geometry)
        return chunks, reason
    except Exception as exc:
        _log_mapping_error(ids, parsed, exc)
        return _run_chunk_text(parsed, headings, config, None), _REASON_MAPPING_ERROR


async def assemble_ingest_request(
    batch_id: str,
    document_id: str,
    filename: str,
    content_type: str,
    raw_content: bytes,
    correlation_id: str,
    config: IngestorConfig,
) -> IngestRequest:
    ids = _RequestIds(batch_id=batch_id, document_id=document_id)
    fmt = _resolve_format(content_type, filename)
    captured = await _capture_asset(fmt, raw_content, config)
    _log_geometry_outcome(ids, captured)

    parsed = captured.parsed
    headings = detect_headings(parsed.text)
    chunks, geometry_reason = _chunk_with_geometry(ids, headings, config, captured)

    entries = build_paper_entries(document_id, parsed.text, chunks)
    provenance_map = build_provenance_map(batch_id, entries)

    return IngestRequest(
        batch_id=batch_id,
        document_id=document_id,
        filename=filename,
        source_kind=SourceKind.PAPER,
        raw_content=raw_content,
        chunks=chunks,
        provenance_map=provenance_map,
        correlation_id=correlation_id,
        viewable_pdf=captured.viewable_pdf,
        geometry_reason=geometry_reason,
    )
