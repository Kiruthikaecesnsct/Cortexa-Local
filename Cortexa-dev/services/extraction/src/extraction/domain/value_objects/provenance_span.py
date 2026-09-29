from typing import Literal

from pydantic import BaseModel, field_validator


class ProvenanceSpan(BaseModel, frozen=True):
    source_kind: Literal["paper", "code"]
    locator: str
    section_hint: str | None = None
    span_start: int | None = None
    span_end: int | None = None
    page_number: int | None = None
    excerpt: str | None = None

    @field_validator("locator")
    @classmethod
    def locator_non_empty(cls, v: str) -> str:
        if not v.strip():
            raise ValueError("locator must not be empty")
        return v
