from pydantic import BaseModel


class PageSpan(BaseModel):
    page_number: int
    start_char: int
    end_char: int


class PageDimensions(BaseModel):
    page_number: int
    width: float
    height: float


class WordBox(BaseModel):
    page_number: int
    text: str
    x0: float
    x1: float
    top: float
    bottom: float


class ParsedDocument(BaseModel):
    text: str
    pages: list[PageSpan]
    page_dimensions: list[PageDimensions] = []
    words: list[WordBox] = []
