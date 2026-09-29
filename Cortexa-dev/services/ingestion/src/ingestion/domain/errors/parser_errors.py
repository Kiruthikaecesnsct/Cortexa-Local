class ParserError(Exception):
    """Base class for all parser failures."""


class UnsupportedFormatError(ParserError):
    """File format is not supported."""


class CorruptedFileError(ParserError):
    """File is corrupted or malformed."""


class ConversionError(ParserError):
    """DOCX to PDF conversion failed."""


class ConversionTimeoutError(ConversionError):
    """DOCX to PDF conversion exceeded its bounded time budget."""
