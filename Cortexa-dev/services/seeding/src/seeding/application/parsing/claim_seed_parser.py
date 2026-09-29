from pydantic import ValidationError

from seeding.domain.errors.seeding_errors import ClaimSeedParseError
from seeding.domain.models.claim_seed import (
    ClaimSeedSet,
    DependentClaimSeed,
    IndependentClaimSeed,
    Limitation,
)
from seeding.domain.models.scored_candidate import ScoredCandidate


def _build_model(model_cls, context: str, **kwargs):
    try:
        return model_cls(**kwargs)
    except ValidationError as exc:
        raise ClaimSeedParseError(f"{context} failed validation: {exc}") from exc


def _parse_limitation(raw: dict, evidence_refs: list[str], context: str) -> Limitation:
    refs = raw.get("evidence_refs", [])
    if not refs:
        raise ClaimSeedParseError(f"limitation in {context} has no evidence_refs")
    for ref in refs:
        if ref not in evidence_refs:
            raise ClaimSeedParseError(
                f"evidence ref '{ref}' in {context} not found in evidence refs"
            )
    return _build_model(Limitation, context, text=raw.get("text", ""), evidence_refs=refs)


def _parse_independent_claim(
    raw: dict, evidence_refs: list[str], index: int
) -> IndependentClaimSeed:
    context = f"independent_claims[{index}]"
    limitations_raw = raw.get("limitations", [])
    if not limitations_raw:
        raise ClaimSeedParseError(f"{context} has no limitations")
    limitations = [
        _parse_limitation(lim, evidence_refs, f"{context}.limitations[{i}]")
        for i, lim in enumerate(limitations_raw)
    ]
    return _build_model(
        IndependentClaimSeed,
        context,
        claim_type=raw.get("claim_type", ""),
        preamble=raw.get("preamble", ""),
        recitations=raw.get("recitations", []),
        limitations=limitations,
    )


def _parse_dependent_claim(
    raw: dict, evidence_refs: list[str], index: int, independent_count: int
) -> DependentClaimSeed:
    context = f"dependent_claims[{index}]"
    parent_index = raw.get("parent_index")
    if not isinstance(parent_index, int) or not (0 <= parent_index < independent_count):
        raise ClaimSeedParseError(
            f"{context} has invalid parent_index '{parent_index}' for "
            f"{independent_count} independent claims"
        )
    added_raw = raw.get("added_limitations", [])
    if not added_raw:
        raise ClaimSeedParseError(f"{context} has no added_limitations")
    added_limitations = [
        _parse_limitation(lim, evidence_refs, f"{context}.added_limitations[{i}]")
        for i, lim in enumerate(added_raw)
    ]
    return _build_model(
        DependentClaimSeed,
        context,
        parent_index=parent_index,
        added_limitations=added_limitations,
    )


def parse_claim_seed_set(
    raw: dict, candidate: ScoredCandidate, evidence_refs: list[str]
) -> ClaimSeedSet:
    independent_raw = raw.get("independent_claims")
    if not independent_raw:
        raise ClaimSeedParseError("response is missing independent_claims")
    dependent_raw = raw.get("dependent_claims")
    if not dependent_raw:
        raise ClaimSeedParseError("response is missing dependent_claims")

    independent_claims = [
        _parse_independent_claim(item, evidence_refs, i) for i, item in enumerate(independent_raw)
    ]
    dependent_claims = [
        _parse_dependent_claim(item, evidence_refs, i, len(independent_claims))
        for i, item in enumerate(dependent_raw)
    ]

    return _build_model(
        ClaimSeedSet,
        "claim_seed_set",
        candidate_id=candidate.candidate_id,
        batch_id=candidate.batch_id,
        document_id=candidate.document_id,
        independent_claims=independent_claims,
        dependent_claims=dependent_claims,
    )
