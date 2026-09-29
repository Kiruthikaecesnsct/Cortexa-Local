from __future__ import annotations

from pydantic import BaseModel

from scoring.domain.enums.agreement_level import AgreementLevel
from scoring.domain.enums.scoring_axis import ScoringAxis
from scoring.domain.models.axis_score import AxisScore
from scoring.domain.models.build_verdict_request import BuildVerdictRequest


class StoredVerdict(BaseModel):
    id: str
    batch_id: str
    job_id: str
    candidate_id: str
    document_id: str
    axes: dict[ScoringAxis, AxisScore]
    composite_score: float
    agreement_level: AgreementLevel
    agreeing_axis_count: int
    grounding_meets_minimum: bool = True
    grounding_source_count: int = 0
    single_reason: str | None = None
    drafted_claim: str = ""
    claim_draft_status: str = ""

    @classmethod
    def build(cls, request: BuildVerdictRequest) -> StoredVerdict:
        verdict_id = f"{request.batch_id}:{request.candidate_id}"
        composite = (
            sum(a.score for a in request.axes.values()) / len(request.axes) if request.axes else 0.0
        )
        return cls(
            id=verdict_id,
            batch_id=request.batch_id,
            job_id=request.job_id,
            candidate_id=request.candidate_id,
            document_id=request.document_id,
            axes=request.axes,
            composite_score=composite,
            agreement_level=request.agreement_level,
            agreeing_axis_count=request.agreeing_axis_count,
            grounding_meets_minimum=request.grounding_meets_minimum,
            grounding_source_count=request.grounding_source_count,
            single_reason=request.single_reason,
        )
