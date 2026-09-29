from pydantic import BaseModel, Field

from harvesting.domain.enums.agreement_flag import AgreementFlag
from harvesting.domain.enums.maturity import Maturity
from harvesting.domain.enums.scoring_axis import ScoringAxis
from harvesting.domain.models.axis_score import AxisScore
from harvesting.domain.models.citation import Citation, ProvenanceLink


class ReportCandidate(BaseModel):
    candidate_id: str
    title: str
    description: str
    claim_text: str = ""
    claim_draft: str = ""
    maturity: Maturity
    rank: int
    weighted_score: float
    axes: dict[ScoringAxis, AxisScore]
    agreement_flag: AgreementFlag
    citations: list[Citation]
    provenance_links: list[ProvenanceLink]
    source_availability: dict[str, bool] = Field(default_factory=dict)
    source_status: dict[str, str] = Field(default_factory=dict)
    evidence_sources: list[dict] = Field(default_factory=list)
