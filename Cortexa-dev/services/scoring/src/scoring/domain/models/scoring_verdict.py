from pydantic import BaseModel

from scoring.domain.enums.scoring_axis import ScoringAxis
from scoring.domain.models.axis_score import AxisScore


class ScoringVerdict(BaseModel):
    batch_id: str
    job_id: str
    candidate_id: str
    document_id: str
    axes: dict[ScoringAxis, AxisScore]
