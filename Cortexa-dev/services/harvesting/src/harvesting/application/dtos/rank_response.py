from pydantic import BaseModel

from harvesting.domain.models.ranked_candidate import RankedCandidate


class RankResponseDto(BaseModel):
    batch_id: str
    ranked: list[RankedCandidate]
