from dataclasses import dataclass, field

from seeding.application.parsing.json_extractor import extract_json
from seeding.domain.errors.seeding_errors import DigestParseError, OpportunityParseError
from seeding.domain.models.digest import CitedEntry

MAP_FIELDS = (
    "claims_made",
    "methods_used",
    "limitations",
    "future_work",
    "key_concepts",
)
REDUCE_ARRAY_FIELDS = (
    "contributions",
    "limitations",
    "future_work",
    "key_concepts",
    "tech_fields",
)


@dataclass
class MapNote:
    claims_made: list[CitedEntry] = field(default_factory=list)
    methods_used: list[CitedEntry] = field(default_factory=list)
    limitations: list[CitedEntry] = field(default_factory=list)
    future_work: list[CitedEntry] = field(default_factory=list)
    key_concepts: list[CitedEntry] = field(default_factory=list)

    def is_empty(self) -> bool:
        return not any(getattr(self, name) for name in MAP_FIELDS)


@dataclass
class ReduceBrief:
    problem_space: CitedEntry = field(default_factory=CitedEntry)
    contributions: list[CitedEntry] = field(default_factory=list)
    limitations: list[CitedEntry] = field(default_factory=list)
    future_work: list[CitedEntry] = field(default_factory=list)
    key_concepts: list[CitedEntry] = field(default_factory=list)
    tech_fields: list[CitedEntry] = field(default_factory=list)

    def is_empty(self) -> bool:
        arrays_empty = not any(getattr(self, name) for name in REDUCE_ARRAY_FIELDS)
        return arrays_empty and not self.problem_space.chunk_ids and not self.problem_space.text


def _filter_entry(entry: object, allowed: set[str]) -> CitedEntry | None:
    if not isinstance(entry, dict):
        return None
    kept = [str(cid) for cid in entry.get("chunk_ids") or [] if str(cid) in allowed]
    if not kept:
        return None
    return CitedEntry(text=str(entry.get("text") or ""), chunk_ids=kept)


def _parse_entries(raw_list: object, allowed: set[str]) -> list[CitedEntry]:
    if not isinstance(raw_list, list):
        return []
    parsed = (_filter_entry(entry, allowed) for entry in raw_list)
    return [entry for entry in parsed if entry is not None]


def _extract(content: str) -> dict:
    try:
        return extract_json(content)
    except OpportunityParseError as exc:
        raise DigestParseError(str(exc)) from exc


def parse_map_note(content: str, allowed: set[str]) -> MapNote:
    raw = _extract(content)
    return MapNote(**{name: _parse_entries(raw.get(name), allowed) for name in MAP_FIELDS})


def _parse_problem_space(raw: object, allowed: set[str]) -> CitedEntry:
    entry = _filter_entry(raw, allowed)
    return entry if entry is not None else CitedEntry()


def parse_reduce_brief(content: str, allowed: set[str]) -> ReduceBrief:
    raw = _extract(content)
    arrays = {name: _parse_entries(raw.get(name), allowed) for name in REDUCE_ARRAY_FIELDS}
    return ReduceBrief(
        problem_space=_parse_problem_space(raw.get("problem_space"), allowed), **arrays
    )
