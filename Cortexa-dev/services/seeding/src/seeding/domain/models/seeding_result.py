from datetime import UTC, datetime

from pydantic import BaseModel, Field, field_validator

from seeding.domain.enums.opportunity_category import OpportunityCategory
from seeding.domain.models.citation import Citation
from seeding.domain.models.ideation import Grounding, RoundRecord
from seeding.domain.models.landscape import LandscapeProvenance
from seeding.domain.validation.prompt_injection import reject_prompt_delimiters


class ReportAxis(BaseModel):
    score: int
    refs: list[str] = Field(default_factory=list)


class PriorArtMatch(BaseModel):
    reference: str = ""
    title: str = ""
    url: str = ""
    source: str = ""
    relevance_score: float = 0.0
    note: str = ""


class ConceptMapEntry(BaseModel):
    concept: str = ""
    density: str = "sparse"
    corpus_axis: str = "sparse"
    live_axis: str = "sparse"
    opportunity_ids: list[str] = Field(default_factory=list)
    whitespace: bool = False


class SeedingOpportunity(BaseModel):
    id: str
    title: str
    description: str
    confidence_score: float
    roadmap_alignment: str
    category: OpportunityCategory | None = None
    innovation_rationale: str = ""
    source_candidate_ids: list[str] = Field(default_factory=list)
    grounded_in: Grounding | None = None
    novelty_delta: str = ""
    mechanism: str = ""
    claim_statement: str = ""
    round_index: int | None = None
    candidate_id: str = ""
    evidence_bundle_id: str = ""
    weighted_score: float | None = None
    axes: dict[str, ReportAxis] = Field(default_factory=dict)
    citations: list[Citation] = Field(default_factory=list)
    source_availability: dict[str, bool] = Field(default_factory=dict)
    source_status: dict[str, str] = Field(default_factory=dict)
    evidence_sources: list[dict] = Field(default_factory=list)
    target_concept: str = ""
    prior_art_proximity: list[PriorArtMatch] = Field(default_factory=list)

    @field_validator(
        "title", "description", "innovation_rationale", "roadmap_alignment", mode="before"
    )
    @classmethod
    def _reject_delimiters(cls, v: object) -> object:
        return reject_prompt_delimiters(v)


class SeedingResult(BaseModel):
    id: str
    batch_id: str
    opportunities: list[SeedingOpportunity]
    created_at: datetime = Field(default_factory=lambda: datetime.now(UTC))
    engine: str = "seeding"
    seeding_mode: str = "legacy"
    document_id: str = ""
    landscape: LandscapeProvenance | None = None
    rounds: list[RoundRecord] = Field(default_factory=list)
    scratchpad_id: str = ""
    explanation: str = ""
    is_empty: bool = False
    concept_map: list[ConceptMapEntry] = Field(default_factory=list)
