from pydantic import BaseModel, Field

from evidence.domain.enums.evidence_source import EvidenceSource
from evidence.domain.enums.patent_source_name import PatentSourceName
from evidence.domain.enums.patent_source_outcome import PatentSourceOutcome


class PatentMatchDto(BaseModel):
    reference: str
    title: str
    applicant: str
    date: str
    url: str
    relevance_score: float
    source: EvidenceSource
    jurisdiction: str
    abstract: str
    claims: list[str] = Field(default_factory=list)


class PatentSourceResultDto(BaseModel):
    source: PatentSourceName
    outcome: PatentSourceOutcome
    hit_count: int
    latency_ms: float
    error_detail: str | None = None


class PatentSearchResponseDto(BaseModel):
    matches: list[PatentMatchDto]
    sources: list[PatentSourceResultDto]
