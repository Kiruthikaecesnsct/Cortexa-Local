from pydantic import BaseModel, Field, field_validator

from seeding.domain.enums.opportunity_category import OpportunityCategory


def _as_text(v: object) -> str:
    return "" if v is None else str(v)


def _as_str_list(v: object) -> list[str]:
    if v is None:
        return []
    if isinstance(v, (str, bytes)):
        return [str(v)]
    return [str(item) for item in v]


class RetrievedExcerpt(BaseModel):
    chunk_id: str = ""
    section_label: str = ""
    text: str = ""

    @field_validator("chunk_id", "section_label", "text", mode="before")
    @classmethod
    def _coerce_text(cls, v: object) -> str:
        return _as_text(v)


class Grounding(BaseModel):
    chunk_ids: list[str] = Field(default_factory=list)
    excerpts: list[RetrievedExcerpt] = Field(default_factory=list)

    @field_validator("chunk_ids", mode="before")
    @classmethod
    def _coerce_chunk_ids(cls, v: object) -> list[str]:
        return _as_str_list(v)

    @field_validator("excerpts", mode="before")
    @classmethod
    def _coerce_excerpts(cls, v: object) -> list:
        return [] if v is None else v


class IdeaSketch(BaseModel):
    sketch_id: str = ""
    title: str = ""
    summary: str = ""
    chunk_ids: list[str] = Field(default_factory=list)
    novelty_delta: str = ""
    target_concept: str = ""

    @field_validator(
        "sketch_id", "title", "summary", "novelty_delta", "target_concept", mode="before"
    )
    @classmethod
    def _coerce_text(cls, v: object) -> str:
        return _as_text(v)

    @field_validator("chunk_ids", mode="before")
    @classmethod
    def _coerce_chunk_ids(cls, v: object) -> list[str]:
        return _as_str_list(v)


class CritiqueVerdict(BaseModel):
    sketch_id: str = ""
    verdict: str = ""
    reason_code: str = ""
    note: str = ""

    @field_validator("sketch_id", "verdict", "reason_code", "note", mode="before")
    @classmethod
    def _coerce_text(cls, v: object) -> str:
        return _as_text(v)


class RefinedIdea(BaseModel):
    sketch_id: str = ""
    title: str = ""
    description: str = ""
    mechanism: str = ""
    claim_statement: str = ""
    category: OpportunityCategory
    novelty_delta: str = ""
    chunk_ids: list[str] = Field(default_factory=list)
    roadmap_alignment: str = ""

    @field_validator(
        "sketch_id",
        "title",
        "description",
        "mechanism",
        "claim_statement",
        "novelty_delta",
        "roadmap_alignment",
        mode="before",
    )
    @classmethod
    def _coerce_text(cls, v: object) -> str:
        return _as_text(v)

    @field_validator("chunk_ids", mode="before")
    @classmethod
    def _coerce_chunk_ids(cls, v: object) -> list[str]:
        return _as_str_list(v)


class RoundRecord(BaseModel):
    round: int = 0
    themes: list[str] = Field(default_factory=list)
    proposed_count: int = 0
    accepted_count: int = 0
    rejected_reasons: list[str] = Field(default_factory=list)

    @field_validator("themes", "rejected_reasons", mode="before")
    @classmethod
    def _coerce_str_list(cls, v: object) -> list[str]:
        return _as_str_list(v)
