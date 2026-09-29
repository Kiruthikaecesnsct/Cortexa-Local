from dataclasses import dataclass

from pydantic import ValidationError

from seeding.application.parsing.candidate_id_normalizer import normalize_candidate_id
from seeding.application.parsing.json_extractor import extract_json
from seeding.domain.errors.seeding_errors import IdeationParseError, OpportunityParseError
from seeding.domain.models.ideation import CritiqueVerdict, IdeaSketch, RefinedIdea

_ACCEPT = "accept"
_REJECT = "reject"
_REJECT_CODES = {"restates_document", "duplicate_accepted", "crowded_zone"}
_VALID_REASON_CODES = _REJECT_CODES | {_ACCEPT}


@dataclass
class ParsedSketches:
    sketches: list[IdeaSketch]
    dropped: list[str]


@dataclass
class ParsedCritique:
    accepted_ids: set[str]
    rejections: list[tuple[str, str]]
    dropped: list[str]


@dataclass
class ParsedRefined:
    opportunities: list[RefinedIdea]
    dropped: list[str]


def _safe_extract(content: str, key: str) -> tuple[list, str | None]:
    try:
        raw = extract_json(content)
    except OpportunityParseError as exc:
        return [], f"extract_failed: {exc}"
    array = raw.get(key)
    if not isinstance(array, list):
        return [], f"missing_or_non_array: {key}"
    return array, None


def _normalized_ids(ids: list[str], allowed: set[str]) -> list[str]:
    return [str(cid) for cid in ids if normalize_candidate_id(cid) in allowed]


def _parse_sketch(item: object, allowed: set[str], idx: int) -> IdeaSketch:
    if not isinstance(item, dict):
        raise IdeationParseError(f"sketch[{idx}] is not an object")
    try:
        sketch = IdeaSketch.model_validate(item)
    except ValidationError as exc:
        raise IdeationParseError(f"sketch[{idx}] invalid: {exc}") from exc
    kept = _normalized_ids(sketch.chunk_ids, allowed)
    if not kept:
        raise IdeationParseError(f"sketch[{idx}] has no valid chunk_ids")
    if not sketch.title.strip() or not sketch.novelty_delta.strip():
        raise IdeationParseError(f"sketch[{idx}] missing title or novelty_delta")
    sketch_id = sketch.sketch_id.strip() or f"s{idx}"
    return sketch.model_copy(update={"chunk_ids": kept, "sketch_id": sketch_id})


def parse_propose(content: str, allowed: set[str], ideas_per_round: int) -> ParsedSketches:
    items, err = _safe_extract(content, "ideas")
    dropped = [err] if err else []
    survivors: list[IdeaSketch] = []
    for idx, item in enumerate(items):
        try:
            survivors.append(_parse_sketch(item, allowed, idx))
        except IdeationParseError as exc:
            dropped.append(str(exc))
    kept = survivors[:ideas_per_round]
    for over in survivors[ideas_per_round:]:
        dropped.append(f"over_cap sketch_id={over.sketch_id}")
    return ParsedSketches(sketches=kept, dropped=dropped)


def _parse_verdict(item: object, sketch_ids: set[str], idx: int) -> CritiqueVerdict:
    if not isinstance(item, dict):
        raise IdeationParseError(f"verdict[{idx}] is not an object")
    try:
        verdict = CritiqueVerdict.model_validate(item)
    except ValidationError as exc:
        raise IdeationParseError(f"verdict[{idx}] invalid: {exc}") from exc
    if verdict.sketch_id not in sketch_ids:
        raise IdeationParseError(f"verdict[{idx}] unknown sketch_id {verdict.sketch_id!r}")
    if verdict.reason_code not in _VALID_REASON_CODES:
        raise IdeationParseError(f"verdict[{idx}] bad reason_code {verdict.reason_code!r}")
    if verdict.verdict == _ACCEPT and verdict.reason_code != _ACCEPT:
        raise IdeationParseError(f"verdict[{idx}] accept requires reason_code accept")
    if verdict.verdict == _REJECT and verdict.reason_code not in _REJECT_CODES:
        raise IdeationParseError(f"verdict[{idx}] reject requires a reject reason_code")
    if verdict.verdict not in {_ACCEPT, _REJECT}:
        raise IdeationParseError(f"verdict[{idx}] bad verdict {verdict.verdict!r}")
    return verdict


def parse_critique(content: str, sketch_ids: set[str]) -> ParsedCritique:
    items, err = _safe_extract(content, "verdicts")
    dropped = [err] if err else []
    accepted: set[str] = set()
    rejections: list[tuple[str, str]] = []
    for idx, item in enumerate(items):
        try:
            verdict = _parse_verdict(item, sketch_ids, idx)
        except IdeationParseError as exc:
            dropped.append(str(exc))
            continue
        if verdict.verdict == _ACCEPT:
            accepted.add(verdict.sketch_id)
        else:
            rejections.append((verdict.sketch_id, verdict.reason_code))
    return ParsedCritique(accepted_ids=accepted, rejections=rejections, dropped=dropped)


def _parse_refined(
    item: object, sketches: dict[str, IdeaSketch], roadmap_supplied: bool, idx: int
) -> RefinedIdea:
    if not isinstance(item, dict):
        raise IdeationParseError(f"opportunity[{idx}] is not an object")
    sketch_id = str(item.get("sketch_id") or "").strip()
    sketch = sketches.get(sketch_id)
    if sketch is None:
        raise IdeationParseError(f"opportunity[{idx}] unknown sketch_id {sketch_id!r}")
    try:
        refined = RefinedIdea.model_validate(item)
    except ValidationError as exc:
        raise IdeationParseError(f"opportunity[{idx}] invalid: {exc}") from exc
    allowed = {normalize_candidate_id(cid) for cid in sketch.chunk_ids}
    kept = _normalized_ids(refined.chunk_ids, allowed)
    if not kept:
        raise IdeationParseError(f"opportunity[{idx}] has no valid chunk_ids")
    alignment = refined.roadmap_alignment if roadmap_supplied else ""
    return refined.model_copy(
        update={"chunk_ids": kept, "sketch_id": sketch_id, "roadmap_alignment": alignment}
    )


def parse_refine(
    content: str, sketches: dict[str, IdeaSketch], roadmap_supplied: bool
) -> ParsedRefined:
    items, err = _safe_extract(content, "opportunities")
    dropped = [err] if err else []
    survivors: list[RefinedIdea] = []
    for idx, item in enumerate(items):
        try:
            survivors.append(_parse_refined(item, sketches, roadmap_supplied, idx))
        except IdeationParseError as exc:
            dropped.append(str(exc))
    return ParsedRefined(opportunities=survivors, dropped=dropped)
