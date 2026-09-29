from pydantic import BaseModel

from seeding.domain.enums.scoring_axis import ScoringAxis
from seeding.domain.models.axis_score import AxisScore


class ScoredCandidate(BaseModel):
    candidate_id: str
    batch_id: str
    job_id: str
    document_id: str
    axes: dict[ScoringAxis, AxisScore]
