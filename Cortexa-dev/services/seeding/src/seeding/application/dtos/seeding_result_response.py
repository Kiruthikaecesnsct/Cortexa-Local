from datetime import datetime

from pydantic import BaseModel, Field

from seeding.domain.models.landscape import LandscapeProvenance, LandscapeSourceFlags
from seeding.domain.models.seeding_result import ConceptMapEntry, SeedingOpportunity


class SeedingResultResponse(BaseModel):
    id: str
    batch_id: str
    opportunities: list[SeedingOpportunity]
    created_at: datetime
    engine: str = "seeding"
    document_id: str = ""
    landscape: LandscapeProvenance | None = None
    source_flags: LandscapeSourceFlags = Field(default_factory=LandscapeSourceFlags)
    concept_map: list[ConceptMapEntry] = Field(default_factory=list)
    explanation: str = ""
    is_empty: bool = False
