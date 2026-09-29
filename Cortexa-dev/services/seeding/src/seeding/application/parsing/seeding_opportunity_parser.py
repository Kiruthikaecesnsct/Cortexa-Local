from dataclasses import dataclass
from uuid import uuid4

from seeding.application.parsing.candidate_id_normalizer import normalize_candidate_id
from seeding.domain.enums.opportunity_category import OpportunityCategory
from seeding.domain.errors.seeding_errors import OpportunityParseError
from seeding.domain.models.seeding_result import SeedingOpportunity


@dataclass
class ParsedOpportunities:
    opportunities: list[SeedingOpportunity]
    dropped: list[str]


def _resolve_source_candidates(ids: list, allowed: set[str], idx: int) -> list[str]:
    """Normalize each model-returned id and keep the ones that ground on a
    known candidate. Salvages an opportunity that carries a mix of valid and
    malformed/unknown ids; rejects one that grounds on nothing.
    """
    if not ids:
        raise OpportunityParseError(f"opportunity[{idx}] has no source_candidate_ids")

    resolved: list[str] = []
    unknown: list = []
    for cid in ids:
        normalized = normalize_candidate_id(cid)
        if normalized in allowed:
            resolved.append(normalized)
        else:
            unknown.append(cid)

    if not resolved:
        raise OpportunityParseError(
            f"opportunity[{idx}] references unknown candidate ids: {unknown}"
        )
    return resolved


def _clamp(value: float) -> float:
    return max(0.0, min(1.0, value))


def _parse_seeding_item(item: dict, allowed: set[str], idx: int) -> SeedingOpportunity:
    raw_ids: list = item.get("source_candidate_ids", [])
    source_ids = _resolve_source_candidates(raw_ids, allowed, idx)

    raw_category = item.get("category", "")
    try:
        category = OpportunityCategory(raw_category)
    except ValueError as exc:
        raise OpportunityParseError(
            f"opportunity[{idx}] has unknown category '{raw_category}'"
        ) from exc

    confidence = _clamp(float(item.get("confidence", 0.0)))

    return SeedingOpportunity(
        id=str(uuid4()),
        title=item.get("title", ""),
        description=item.get("description", ""),
        confidence_score=confidence,
        roadmap_alignment=item.get("roadmap_integration", ""),
        category=category,
        innovation_rationale=item.get("innovation_rationale", ""),
        source_candidate_ids=source_ids,
    )


def _require_survivors(opportunities: list, dropped: list[str]) -> None:
    if opportunities:
        return
    drop_summary = "; ".join(dropped) if dropped else "all items invalid"
    raise OpportunityParseError(f"zero opportunities survived parsing: {drop_summary}")


def parse_seeding_opportunities(raw: dict, candidate_ids: set[str]) -> ParsedOpportunities:
    raw_list = raw.get("opportunities")
    if not raw_list:
        raise OpportunityParseError("response contains no opportunities")

    allowed = {normalize_candidate_id(cid) for cid in candidate_ids}
    opportunities = []
    dropped = []

    for idx, item in enumerate(raw_list):
        try:
            opportunities.append(_parse_seeding_item(item, allowed, idx))
        except OpportunityParseError as e:
            dropped.append(str(e))

    _require_survivors(opportunities, dropped)
    return ParsedOpportunities(opportunities=opportunities, dropped=dropped)
