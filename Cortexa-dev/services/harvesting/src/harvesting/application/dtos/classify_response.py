from pydantic import BaseModel

from harvesting.domain.enums.maturity import Maturity


class ClassifyResponseDto(BaseModel):
    candidate_id: str
    maturity: Maturity
    novelty_score: int
    feasibility_score: int
    reasoning: str
