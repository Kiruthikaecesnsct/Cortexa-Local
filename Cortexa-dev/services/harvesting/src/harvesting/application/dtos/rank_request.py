from pydantic import BaseModel

from harvesting.domain.enums.scoring_axis import ScoringAxis
from harvesting.domain.models.axis_score import AxisScore


class RankCandidateDto(BaseModel):
    candidate_id: str
    document_id: str
    axes: dict[ScoringAxis, AxisScore]


class RankRequestDto(BaseModel):
    batch_id: str
    job_id: str
    candidates: list[RankCandidateDto]
