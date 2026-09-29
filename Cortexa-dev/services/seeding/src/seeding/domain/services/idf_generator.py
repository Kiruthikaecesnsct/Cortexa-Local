from seeding.domain.enums.scoring_axis import ScoringAxis
from seeding.domain.errors.seeding_errors import AbstractTooLongError, UngroundedSeedingError
from seeding.domain.models.evidence_bundle import EvidenceBundle
from seeding.domain.models.opportunity_map import OpportunityMap
from seeding.domain.models.scored_candidate import ScoredCandidate

_REQUIRED_AXES = set(ScoringAxis)


def validate_inputs(
    candidate: ScoredCandidate, opportunity_map: OpportunityMap, bundle: EvidenceBundle
) -> None:
    if not bundle.hits:
        raise UngroundedSeedingError("evidence bundle contains no hits")
    missing = _REQUIRED_AXES - set(candidate.axes.keys())
    if missing:
        raise UngroundedSeedingError(f"candidate is missing axes: {sorted(missing)}")
    if not any(opportunity_map.opportunities.values()):
        raise UngroundedSeedingError("opportunity map contains no opportunities")


def validate_abstract_length(text: str, max_words: int = 250) -> None:
    word_count = len(text.split())
    if word_count > max_words:
        raise AbstractTooLongError(f"abstract has {word_count} words, exceeds limit of {max_words}")
