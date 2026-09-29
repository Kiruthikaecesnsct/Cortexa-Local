from __future__ import annotations

from pydantic import BaseModel

from seeding.domain.enums.opportunity_category import OpportunityCategory
from seeding.domain.models.opportunity import Opportunity
from seeding.domain.models.opportunity_map import OpportunityMap


class GenerateMapResponse(BaseModel):
    candidate_id: str
    batch_id: str
    document_id: str
    opportunities: dict[OpportunityCategory, list[Opportunity]]

    @classmethod
    def from_map(cls, opp_map: OpportunityMap) -> GenerateMapResponse:
        return cls(
            candidate_id=opp_map.candidate_id,
            batch_id=opp_map.batch_id,
            document_id=opp_map.document_id,
            opportunities=opp_map.opportunities,
        )
