from datetime import datetime

from pydantic import BaseModel

from evidence.domain.enums.confidence_band import ConfidenceBand
from evidence.domain.enums.evidence_source import EvidenceSource
from evidence.domain.enums.patent_source_name import PatentSourceName
from evidence.domain.enums.source_status import SourceStatus
from evidence.domain.models.evidence_hit import EvidenceHit
from evidence.domain.models.patent_source_result import PatentSourceResult


class LlmResearchSummary(BaseModel):
    findings: list[str]
    confidence: float


class EvidenceBundle(BaseModel):
    id: str
    batch_id: str
    job_id: str
    candidate_id: str
    document_id: str
    hits: list[EvidenceHit]
    confidence_band: ConfidenceBand
    sources_used: list[EvidenceSource]
    source_flags: dict[EvidenceSource, bool]
    source_status: dict[EvidenceSource, SourceStatus] = {}
    merged_at: datetime
    patent_source_results: list[PatentSourceResult] = []
    degraded: bool = False
    degraded_sources: list[PatentSourceName] = []
    active_source_count: int = 0
    meets_minimum_sources: bool = True
    minimum_active_sources: int = 0
    llm_research: LlmResearchSummary | None = None
