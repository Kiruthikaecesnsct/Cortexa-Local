from seeding.domain.enums.scoring_axis import ScoringAxis
from seeding.domain.errors.seeding_errors import (
    InsufficientLatticeError,
    UngroundedSeedingError,
)
from seeding.domain.models.evidence_bundle import EvidenceBundle
from seeding.domain.models.idf_draft import IdfDraft
from seeding.domain.models.invention_lattice import InventionLattice
from seeding.domain.models.scored_candidate import ScoredCandidate

_REQUIRED_AXES = set(ScoringAxis)
_MIN_LEVEL_ENTRIES = 2


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


def _validate_level_count(name: str, entries: list) -> None:
    if len(entries) < _MIN_LEVEL_ENTRIES:
        raise InsufficientLatticeError(
            f"expected at least {_MIN_LEVEL_ENTRIES} entries in '{name}', got {len(entries)}"
        )


def validate_lattice(lattice: InventionLattice) -> None:
    if lattice.core is None:
        raise InsufficientLatticeError("lattice is missing the core entry")
    _validate_level_count("continuations", lattice.continuations)
    _validate_level_count("platform", lattice.platform)
    _validate_level_count("system", lattice.system)
