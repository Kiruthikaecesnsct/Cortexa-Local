from seeding.domain.enums.scoring_axis import ScoringAxis
from seeding.domain.errors.seeding_errors import (
    InsufficientClaimSeedsError,
    UngroundedSeedingError,
)
from seeding.domain.models.claim_seed import ClaimSeedSet
from seeding.domain.models.evidence_bundle import EvidenceBundle
from seeding.domain.models.idf_draft import IdfDraft
from seeding.domain.models.scored_candidate import ScoredCandidate

_REQUIRED_AXES = set(ScoringAxis)
_MIN_INDEPENDENT_CLAIMS = 1
_MIN_DEPENDENT_CLAIMS = 2


def validate_inputs(
    candidate: ScoredCandidate, idf_draft: IdfDraft, bundle: EvidenceBundle
) -> None:
    if not bundle.hits:
        raise UngroundedSeedingError("evidence bundle contains no hits")
    missing = _REQUIRED_AXES - set(candidate.axes.keys())
    if missing:
        raise UngroundedSeedingError(f"candidate is missing axes: {sorted(missing)}")
    if not idf_draft.abstract.text or not idf_draft.summary.text:
        raise UngroundedSeedingError("IDF abstract or summary is empty")
    if not idf_draft.core_differentiating_feature.text:
        raise UngroundedSeedingError("IDF core differentiating feature is empty")


def validate_seed_counts(seed_set: ClaimSeedSet) -> None:
    if len(seed_set.independent_claims) < _MIN_INDEPENDENT_CLAIMS:
        raise InsufficientClaimSeedsError(
            f"expected at least {_MIN_INDEPENDENT_CLAIMS} independent claim, "
            f"got {len(seed_set.independent_claims)}"
        )
    if len(seed_set.dependent_claims) < _MIN_DEPENDENT_CLAIMS:
        raise InsufficientClaimSeedsError(
            f"expected at least {_MIN_DEPENDENT_CLAIMS} dependent claims, "
            f"got {len(seed_set.dependent_claims)}"
        )
