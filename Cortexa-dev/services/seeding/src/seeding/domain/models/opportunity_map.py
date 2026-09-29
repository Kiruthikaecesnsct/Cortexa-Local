from pydantic import BaseModel

from seeding.domain.enums.opportunity_category import OpportunityCategory
from seeding.domain.models.opportunity import Opportunity


class OpportunityMap(BaseModel):
    candidate_id: str
    batch_id: str
    document_id: str
    opportunities: dict[OpportunityCategory, list[Opportunity]]
