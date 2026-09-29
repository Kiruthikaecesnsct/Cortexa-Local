from pydantic import BaseModel

from harvesting.domain.enums.scoring_axis import ScoringAxis
from harvesting.domain.models.axis_score import AxisScore


class ClassifyRequestDto(BaseModel):
    candidate_id: str
    batch_id: str
    job_id: str
    document_id: str
    axes: dict[ScoringAxis, AxisScore]
