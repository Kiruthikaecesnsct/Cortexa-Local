from pydantic import BaseModel

from scoring.domain.models.dual_scoring_verdict import DualScoringVerdict


class StoreVerdictRequestDto(BaseModel):
    dual_verdict: DualScoringVerdict
    correlation_id: str | None = None
