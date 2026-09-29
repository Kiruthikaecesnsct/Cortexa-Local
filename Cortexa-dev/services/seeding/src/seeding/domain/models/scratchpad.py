from datetime import UTC, datetime
from typing import Literal

from pydantic import BaseModel, Field, field_validator

from seeding.domain.models.ideation import RetrievedExcerpt, RoundRecord


class AcceptedIdea(BaseModel):
    title: str = ""
    summary: str = ""
    novelty_delta: str = ""
    chunk_ids: list[str] = Field(default_factory=list)
    description: str = ""
    mechanism: str = ""
    claim_statement: str = ""
    category: str = ""
    roadmap_alignment: str = ""
    target_concept: str = ""
    round_index: int = 0
    excerpts: list[RetrievedExcerpt] = Field(default_factory=list)

    @field_validator(
        "title",
        "summary",
        "novelty_delta",
        "description",
        "mechanism",
        "claim_statement",
        "category",
        "roadmap_alignment",
        "target_concept",
        mode="before",
    )
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

    @field_validator("excerpts", mode="before")
    @classmethod
    def _coerce_excerpts(cls, v: object) -> list:
        return [] if v is None else v


class RejectedIdea(BaseModel):
    summary: str = ""
    reason_code: str = ""
    detail: str = ""

    @field_validator("summary", "reason_code", "detail", mode="before")
    @classmethod
    def _coerce_text(cls, v: object) -> str:
        return "" if v is None else str(v)


class IdeationScratchpad(BaseModel):
    id: str
    batch_id: str
    document_id: str
    type: str = "ideation_scratchpad"
    schema_version: str
    prompt_version: str = ""
    rounds_completed: int = 0
    accepted_ideas: list[AcceptedIdea] = Field(default_factory=list)
    rejections: list[RejectedIdea] = Field(default_factory=list)
    covered_concepts: list[str] = Field(default_factory=list)
    rounds: list[RoundRecord] = Field(default_factory=list)
    status: Literal["in_progress", "exhausted", "complete"] = "in_progress"
    created_at: datetime = Field(default_factory=lambda: datetime.now(UTC))
    updated_at: datetime = Field(default_factory=lambda: datetime.now(UTC))

    @field_validator("accepted_ideas", "rejections", "rounds", mode="before")
    @classmethod
    def _coerce_list(cls, v: object) -> list:
        return [] if v is None else v

    @field_validator("covered_concepts", mode="before")
    @classmethod
    def _coerce_covered_concepts(cls, v: object) -> list[str]:
        if v is None:
            return []
        if isinstance(v, (str, bytes)):
            return [str(v)]
        return [str(item) for item in v]
