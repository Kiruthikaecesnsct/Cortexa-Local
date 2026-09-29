import asyncio

from ingestion.application.parsers.docx_parser import parse_docx
from ingestion.application.parsers.pdf_parser import parse_pdf
from ingestion.domain.enums.document_format import DocumentFormat
from ingestion.domain.errors.parser_errors import UnsupportedFormatError
from ingestion.domain.models.parsed_document import ParsedDocument


async def parse_document(data: bytes, fmt: DocumentFormat) -> ParsedDocument:
    if fmt == DocumentFormat.PDF:
        return await asyncio.to_thread(parse_pdf, data)
    if fmt == DocumentFormat.DOCX:
        return await asyncio.to_thread(parse_docx, data)
    raise UnsupportedFormatError(f"Unsupported format: {fmt}")
