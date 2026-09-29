from pydantic import BaseModel

from evidence.domain.enums.patent_source_name import PatentSourceName
from evidence.domain.enums.patent_source_outcome import PatentSourceOutcome


class PatentSourceResult(BaseModel):
    source: PatentSourceName
    outcome: PatentSourceOutcome
    hit_count: int
    latency_ms: float
    error_detail: str | None = None
