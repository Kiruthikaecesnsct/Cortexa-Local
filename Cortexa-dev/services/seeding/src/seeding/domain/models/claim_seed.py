from typing import Literal

from pydantic import BaseModel


class Limitation(BaseModel):
    text: str
    evidence_refs: list[str]


class IndependentClaimSeed(BaseModel):
    claim_type: Literal["method", "apparatus"]
    preamble: str
    recitations: list[str]
    limitations: list[Limitation]


class DependentClaimSeed(BaseModel):
    parent_index: int
    added_limitations: list[Limitation]


class ClaimSeedSet(BaseModel):
    candidate_id: str
    batch_id: str
    document_id: str
    independent_claims: list[IndependentClaimSeed]
    dependent_claims: list[DependentClaimSeed]
