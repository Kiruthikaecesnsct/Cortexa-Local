from pydantic import ValidationError

from seeding.domain.errors.seeding_errors import LatticeParseError
from seeding.domain.models.invention_lattice import InventionLattice, LatticeEntry
from seeding.domain.models.scored_candidate import ScoredCandidate

_LEVEL_NAMES = ("continuations", "platform", "system")


def _build_model(model_cls, context: str, **kwargs):
    try:
        return model_cls(**kwargs)
    except ValidationError as exc:
        raise LatticeParseError(f"{context} failed validation: {exc}") from exc


def _parse_entry(raw: dict, evidence_refs: list[str], context: str) -> LatticeEntry:
    refs = raw.get("evidence_refs", [])
    if not refs:
        raise LatticeParseError(f"{context} has no evidence_refs")
    for ref in refs:
        if ref not in evidence_refs:
            raise LatticeParseError(f"evidence ref '{ref}' in {context} not found in evidence refs")
    return _build_model(
        LatticeEntry,
        context,
        title=raw.get("title", ""),
        description=raw.get("description", ""),
        scope=raw.get("scope", ""),
        filing_strategy_note=raw.get("filing_strategy_note", ""),
        evidence_refs=refs,
    )


def _parse_level(raw: dict, evidence_refs: list[str], level_name: str) -> list[LatticeEntry]:
    entries_raw = raw.get(level_name)
    if not entries_raw:
        raise LatticeParseError(f"response is missing '{level_name}'")
    return [
        _parse_entry(item, evidence_refs, f"{level_name}[{i}]")
        for i, item in enumerate(entries_raw)
    ]


def parse_lattice(
    raw: dict, candidate: ScoredCandidate, evidence_refs: list[str]
) -> InventionLattice:
    core_raw = raw.get("core")
    if not core_raw:
        raise LatticeParseError("response is missing 'core'")
    core = _parse_entry(core_raw, evidence_refs, "core")

    levels = {name: _parse_level(raw, evidence_refs, name) for name in _LEVEL_NAMES}

    return _build_model(
        InventionLattice,
        "invention_lattice",
        document_id=candidate.document_id,
        candidate_id=candidate.candidate_id,
        batch_id=candidate.batch_id,
        core=core,
        continuations=levels["continuations"],
        platform=levels["platform"],
        system=levels["system"],
    )
