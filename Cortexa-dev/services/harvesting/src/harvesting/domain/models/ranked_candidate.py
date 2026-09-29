from pydantic import BaseModel


class RankedCandidate(BaseModel):
    candidate_id: str
    batch_id: str
    document_id: str
    rank: int
    weighted_score: float
    novelty_score: int
    feasibility_score: int
    strategic_score: int
    patentability_score: int
    rationale: str
