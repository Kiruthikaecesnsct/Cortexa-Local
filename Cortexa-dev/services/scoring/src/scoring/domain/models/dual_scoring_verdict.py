from pydantic import BaseModel

from scoring.domain.enums.agreement_level import AgreementLevel
from scoring.domain.models.scoring_verdict import ScoringVerdict


class DualScoringVerdict(BaseModel):
    primary: ScoringVerdict
    secondary: ScoringVerdict | None
    agreement_level: AgreementLevel
    agreeing_axis_count: int
    single_reason: str | None = None
