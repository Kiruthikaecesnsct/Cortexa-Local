from datetime import datetime

from pydantic import BaseModel

from scoring.domain.enums.confidence_band import ConfidenceBand
from scoring.domain.enums.evidence_source import EvidenceSource
from scoring.domain.models.evidence_hit import EvidenceHit


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
    merged_at: datetime
    active_source_count: int = 0
    meets_minimum_sources: bool = False
    minimum_active_sources: int = 0
