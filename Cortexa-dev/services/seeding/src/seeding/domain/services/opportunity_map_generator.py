from seeding.domain.enums.scoring_axis import ScoringAxis
from seeding.domain.errors.seeding_errors import UngroundedSeedingError
from seeding.domain.models.evidence_bundle import EvidenceBundle
from seeding.domain.models.scored_candidate import ScoredCandidate

_REQUIRED_AXES = set(ScoringAxis)


def validate_inputs(candidate: ScoredCandidate, bundle: EvidenceBundle) -> None:
    if not bundle.hits:
        raise UngroundedSeedingError("evidence bundle contains no hits")
    missing = _REQUIRED_AXES - set(candidate.axes.keys())
    if missing:
        raise UngroundedSeedingError(f"candidate is missing axes: {sorted(missing)}")
