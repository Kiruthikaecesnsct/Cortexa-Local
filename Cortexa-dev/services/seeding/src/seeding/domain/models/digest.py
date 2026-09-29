from datetime import UTC, datetime

from pydantic import BaseModel, Field, field_validator


class CitedEntry(BaseModel):
    text: str = ""
    chunk_ids: list[str] = Field(default_factory=list)

    @field_validator("text", mode="before")
    @classmethod
    def _coerce_text(cls, v: object) -> str:
        return "" if v is None else str(v)

    @field_validator("chunk_ids", mode="before")
    @classmethod
    def _coerce_chunk_ids(cls, v: object) -> list[str]:
        if v is None:
            return []
        if isinstance(v, (str, bytes)):
            return [str(v)]
        return [str(item) for item in v]


def _empty_problem_space() -> CitedEntry:
    return CitedEntry()


class SectionNote(BaseModel):
    id: str
    batch_id: str
    document_id: str
    type: str = "digest_section_note"
    section_key: str
    section_label: str = ""
    chunk_ids: list[str] = Field(default_factory=list)
    claims_made: list[CitedEntry] = Field(default_factory=list)
    methods_used: list[CitedEntry] = Field(default_factory=list)
    limitations: list[CitedEntry] = Field(default_factory=list)
    future_work: list[CitedEntry] = Field(default_factory=list)
    key_concepts: list[CitedEntry] = Field(default_factory=list)
    schema_version: str
    prompt_version: str
    created_at: datetime = Field(default_factory=lambda: datetime.now(UTC))

    @field_validator(
        "chunk_ids",
        "claims_made",
        "methods_used",
        "limitations",
        "future_work",
        "key_concepts",
        mode="before",
    )
    @classmethod
    def _coerce_list(cls, v: object) -> list:
        return [] if v is None else v


class InventionContextBrief(BaseModel):
    id: str
    batch_id: str
    document_id: str
    type: str = "invention_context_brief"
    is_empty: bool = False
    problem_space: CitedEntry = Field(default_factory=_empty_problem_space)
    contributions: list[CitedEntry] = Field(default_factory=list)
    limitations: list[CitedEntry] = Field(default_factory=list)
    future_work: list[CitedEntry] = Field(default_factory=list)
    key_concepts: list[CitedEntry] = Field(default_factory=list)
    tech_fields: list[CitedEntry] = Field(default_factory=list)
    schema_version: str
    prompt_version: str
    created_at: datetime = Field(default_factory=lambda: datetime.now(UTC))

    @field_validator("problem_space", mode="before")
    @classmethod
    def _coerce_problem_space(cls, v: object) -> object:
        return _empty_problem_space() if v is None else v

    @field_validator(
        "contributions",
        "limitations",
        "future_work",
        "key_concepts",
        "tech_fields",
        mode="before",
    )
    @classmethod
    def _coerce_list(cls, v: object) -> list:
        return [] if v is None else v


class DigestReduceIntermediate(InventionContextBrief):
    type: str = "digest_reduce_intermediate"
    reduce_key: str
    depth: int = 0
    part_index: int = 0
