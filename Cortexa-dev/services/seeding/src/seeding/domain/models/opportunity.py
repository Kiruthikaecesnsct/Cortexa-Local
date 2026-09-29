from pydantic import BaseModel, field_validator

from seeding.domain.enums.opportunity_category import OpportunityCategory
from seeding.domain.validation.prompt_injection import reject_prompt_delimiters


class Opportunity(BaseModel):
    category: OpportunityCategory
    description: str
    justification: str
    citations: list[str]

    @field_validator("description", "justification", mode="before")
    @classmethod
    def _reject_prompt_delimiters(cls, v: object) -> object:
        return reject_prompt_delimiters(v)
