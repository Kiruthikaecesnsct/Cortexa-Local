from seeding.domain.enums.opportunity_category import OpportunityCategory
from seeding.domain.errors.seeding_errors import OpportunityParseError
from seeding.domain.models.opportunity import Opportunity
from seeding.domain.models.opportunity_map import OpportunityMap
from seeding.domain.models.scored_candidate import ScoredCandidate

_REQUIRED_CATEGORIES = set(OpportunityCategory)


def _validate_citation(citation: str, evidence_refs: list[str], category: str) -> None:
    if citation not in evidence_refs:
        raise OpportunityParseError(
            f"citation '{citation}' in category '{category}' not found in evidence refs"
        )


def _parse_opportunity(raw_opp: dict, category: str, evidence_refs: list[str]) -> Opportunity:
    citations = raw_opp.get("citations", [])
    if not citations:
        raise OpportunityParseError(f"opportunity in '{category}' has no citations")
    for citation in citations:
        _validate_citation(citation, evidence_refs, category)
    return Opportunity(
        category=OpportunityCategory(category),
        description=raw_opp.get("description", ""),
        justification=raw_opp.get("justification", ""),
        citations=citations,
    )


def _parse_category(
    raw: dict, category: OpportunityCategory, evidence_refs: list[str]
) -> list[Opportunity]:
    raw_list = raw.get(category.value)
    if raw_list is None:
        raise OpportunityParseError(f"category '{category}' missing from response")
    if not raw_list:
        raise OpportunityParseError(f"category '{category}' has no opportunities")
    return [_parse_opportunity(opp, category.value, evidence_refs) for opp in raw_list]


def parse_opportunity_map(
    raw: dict, candidate: ScoredCandidate, evidence_refs: list[str]
) -> OpportunityMap:
    opportunities: dict[OpportunityCategory, list[Opportunity]] = {}
    for category in OpportunityCategory:
        opportunities[category] = _parse_category(raw, category, evidence_refs)
    return OpportunityMap(
        candidate_id=candidate.candidate_id,
        batch_id=candidate.batch_id,
        document_id=candidate.document_id,
        opportunities=opportunities,
    )
