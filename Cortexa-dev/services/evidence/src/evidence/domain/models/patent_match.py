from pydantic import BaseModel, Field, field_validator

from evidence.domain.enums.evidence_source import EvidenceSource


class PatentMatch(BaseModel):
    reference: str
    title: str
    applicant: str
    date: str
    url: str
    relevance_score: float
    source: EvidenceSource = EvidenceSource.PatentApi
    jurisdiction: str = ""
    abstract: str = ""
    claims: list[str] = Field(default_factory=list)

    @field_validator("relevance_score")
    @classmethod
    def _validate_score(cls, v: float) -> float:
        if not 0.0 <= v <= 1.0:
            raise ValueError(f"relevance_score must be in [0.0, 1.0], got {v}")
        return v
