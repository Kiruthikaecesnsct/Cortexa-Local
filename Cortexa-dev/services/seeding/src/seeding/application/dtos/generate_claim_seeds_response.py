from __future__ import annotations

from pydantic import BaseModel

from seeding.domain.models.claim_seed import (
    ClaimSeedSet,
    DependentClaimSeed,
    IndependentClaimSeed,
)


class GenerateClaimSeedsResponse(BaseModel):
    candidate_id: str
    batch_id: str
    document_id: str
    independent_claims: list[IndependentClaimSeed]
    dependent_claims: list[DependentClaimSeed]

    @classmethod
    def from_seed_set(cls, seed_set: ClaimSeedSet) -> GenerateClaimSeedsResponse:
        return cls(
            candidate_id=seed_set.candidate_id,
            batch_id=seed_set.batch_id,
            document_id=seed_set.document_id,
            independent_claims=seed_set.independent_claims,
            dependent_claims=seed_set.dependent_claims,
        )
