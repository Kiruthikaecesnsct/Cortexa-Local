import io
import logging

import pdfplumber

from ingestion.application.text_normalizer import normalize_text
from ingestion.domain.errors.parser_errors import CorruptedFileError
from ingestion.domain.models.parsed_document import (
    PageDimensions,
    PageSpan,
    ParsedDocument,
    WordBox,
)

logger = logging.getLogger(__name__)

_WORD_X_TOLERANCE = 1.5
_WORD_Y_TOLERANCE = 3.0


def _clamp01(value: float) -> float:
    return max(0.0, min(1.0, value))


def _extract_page_words(page, page_number: int) -> list[WordBox]:
    try:
        raw_words = page.extract_words(
            x_tolerance=_WORD_X_TOLERANCE,
            y_tolerance=_WORD_Y_TOLERANCE,
            keep_blank_chars=False,
        )
    except Exception as exc:
        logger.warning(
            "page_number=%s reason=word_extraction_failed error_type=%s",
            page_number,
            type(exc).__name__,
        )
        return []

    width = page.width or 0.0
    height = page.height or 0.0
    if width <= 0 or height <= 0:
        return []

    words: list[WordBox] = []
    for w in raw_words:
        words.append(
            WordBox(
                page_number=page_number,
                text=w["text"],
                x0=_clamp01(w["x0"] / width),
                x1=_clamp01(w["x1"] / width),
                top=_clamp01(w["top"] / height),
                bottom=_clamp01(w["bottom"] / height),
            )
        )
    return words


def _build_page_spans(page_texts: list[str]) -> list[PageSpan]:
    page_spans: list[PageSpan] = []
    current_offset = 0

    for page_num, page_text in enumerate(page_texts, start=1):
        page_len = len(page_text)
        if page_len > 0:
            page_spans.append(
                PageSpan(
                    page_number=page_num,
                    start_char=current_offset,
                    end_char=current_offset + page_len,
                )
            )
        current_offset += page_len
        if page_num < len(page_texts):
            current_offset += 1

    return page_spans


def parse_pdf(data: bytes) -> ParsedDocument:
    try:
        with pdfplumber.open(io.BytesIO(data)) as pdf:
            raw_texts = [page.extract_text() or "" for page in pdf.pages]
            page_dimensions = [
                PageDimensions(page_number=i, width=page.width or 0.0, height=page.height or 0.0)
                for i, page in enumerate(pdf.pages, start=1)
            ]
            words = [
                word
                for i, page in enumerate(pdf.pages, start=1)
                for word in _extract_page_words(page, i)
            ]
    except Exception as e:
        raise CorruptedFileError(f"PDF parsing failed: {e}") from e

    page_texts = [normalize_text(p) for p in raw_texts]
    full_text = "\n".join(page_texts)
    page_spans = _build_page_spans(page_texts)

    return ParsedDocument(
        text=full_text,
        pages=page_spans,
        page_dimensions=page_dimensions,
        words=words,
    )
