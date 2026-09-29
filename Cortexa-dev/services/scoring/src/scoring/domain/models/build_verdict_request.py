from __future__ import annotations

from dataclasses import dataclass

from scoring.domain.enums.agreement_level import AgreementLevel
from scoring.domain.enums.scoring_axis import ScoringAxis
from scoring.domain.models.axis_score import AxisScore


@dataclass(frozen=True)
class BuildVerdictRequest:
    batch_id: str
    job_id: str
    candidate_id: str
    document_id: str
    axes: dict[ScoringAxis, AxisScore]
    agreement_level: AgreementLevel
    agreeing_axis_count: int
    grounding_meets_minimum: bool
    grounding_source_count: int
    single_reason: str | None = None
