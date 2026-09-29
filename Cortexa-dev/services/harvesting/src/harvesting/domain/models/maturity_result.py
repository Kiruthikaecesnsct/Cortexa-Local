from dataclasses import dataclass

from pydantic import BaseModel

from harvesting.domain.enums.maturity import Maturity


@dataclass(frozen=True)
class MaturityBuildParams:
    candidate_id: str
    batch_id: str
    document_id: str
    maturity: Maturity
    novelty_score: int
    feasibility_score: int
    novelty_threshold: int
    feasibility_threshold: int


class MaturityResult(BaseModel):
    id: str
    candidate_id: str
    batch_id: str
    document_id: str
    maturity: Maturity
    novelty_score: int
    feasibility_score: int
    reasoning: str

    @classmethod
    def build(cls, params: MaturityBuildParams) -> MaturityResult:
        reasoning = (
            f"novelty={params.novelty_score} (threshold >={params.novelty_threshold}), "
            f"feasibility={params.feasibility_score} (threshold >={params.feasibility_threshold})"
        )
        return cls(
            id=f"{params.batch_id}:{params.candidate_id}",
            candidate_id=params.candidate_id,
            batch_id=params.batch_id,
            document_id=params.document_id,
            maturity=params.maturity,
            novelty_score=params.novelty_score,
            feasibility_score=params.feasibility_score,
            reasoning=reasoning,
        )
