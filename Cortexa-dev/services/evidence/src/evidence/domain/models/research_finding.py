from pydantic import BaseModel, field_validator

from evidence.domain.enums.evidence_source import EvidenceSource


def _validate_unit_interval(value: float) -> float:
    if not 0.0 <= value <= 1.0:
        raise ValueError(f"confidence must be in [0.0, 1.0], got {value}")
    return value


class Citation(BaseModel):
    """A single prior-art reference the LLM surfaced, with its own confidence.

    Confidence is per-citation so each evidence chip reflects how strongly the
    model tied that specific reference to the candidate — not one blanket score.
    """

    id: str
    confidence: float

    @field_validator("confidence")
    @classmethod
    def _validate_confidence(cls, v: float) -> float:
        return _validate_unit_interval(v)


class ResearchFinding(BaseModel):
    findings: list[str]
    confidence: float
    citations: list[Citation]
    source: EvidenceSource = EvidenceSource.LlmResearch

    @field_validator("confidence")
    @classmethod
    def _validate_confidence(cls, v: float) -> float:
        return _validate_unit_interval(v)
