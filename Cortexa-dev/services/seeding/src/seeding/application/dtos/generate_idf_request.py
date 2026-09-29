from pydantic import BaseModel, field_validator

from seeding.domain.enums.opportunity_category import OpportunityCategory
from seeding.domain.enums.scoring_axis import ScoringAxis
from seeding.domain.models.axis_score import AxisScore
from seeding.domain.models.evidence_bundle import EvidenceHit
from seeding.domain.models.opportunity import Opportunity
from seeding.domain.validation.prompt_injection import reject_prompt_delimiters


class GenerateIdfRequest(BaseModel):
    candidate_id: str
    batch_id: str
    job_id: str
    document_id: str
    axes: dict[ScoringAxis, AxisScore]
    opportunities: dict[OpportunityCategory, list[Opportunity]]
    bundle_id: str
    hits: list[EvidenceHit]
    source_flags: list[str]
    ai_model: str | None = None

    @field_validator("candidate_id", "batch_id", "document_id", mode="before")
    @classmethod
    def _reject_prompt_injection(cls, v: object) -> object:
        return reject_prompt_delimiters(v)
