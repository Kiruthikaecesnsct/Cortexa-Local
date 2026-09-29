from pydantic import BaseModel, field_validator

from harvesting.domain.enums.scoring_axis import ScoringAxis


class AxisScore(BaseModel):
    axis: ScoringAxis
    score: int
    refs: list[str]

    @field_validator("score")
    @classmethod
    def _validate_score(cls, v: int) -> int:
        if not 0 <= v <= 100:
            raise ValueError(f"score must be in [0, 100], got {v}")
        return v
