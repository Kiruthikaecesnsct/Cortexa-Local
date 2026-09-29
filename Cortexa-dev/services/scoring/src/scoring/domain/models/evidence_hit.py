from pydantic import BaseModel, Field, field_validator

from scoring.domain.enums.evidence_source import EvidenceSource


class EvidenceHit(BaseModel):
    patent_id: str | None
    content_hash: str
    title: str
    citation: str
    url: str
    similarity: float
    sources: set[EvidenceSource]
    abstract: str = ""
    claims: list[str] = Field(default_factory=list)
    jurisdiction: str = ""

    @field_validator("similarity")
    @classmethod
    def _validate_similarity(cls, v: float) -> float:
        if not 0.0 <= v <= 1.0:
            raise ValueError(f"similarity must be in [0.0, 1.0], got {v}")
        return v
