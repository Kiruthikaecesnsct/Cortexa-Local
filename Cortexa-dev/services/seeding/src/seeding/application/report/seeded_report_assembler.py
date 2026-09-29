from seeding.domain.enums.opportunity_category import OpportunityCategory
from seeding.domain.models.ideation import Grounding
from seeding.domain.models.landscape import (
    ConceptLandscape,
    CorpusMatch,
    LiveMatch,
    PriorArtLandscape,
)
from seeding.domain.models.seeding_result import (
    ConceptMapEntry,
    PriorArtMatch,
    ReportAxis,
    SeedingOpportunity,
)
from seeding.domain.services.evidence_citation_resolver import (
    resolve_citations,
    resolve_evidence_source_views,
    resolve_source_availability,
    resolve_source_status,
)

_CATEGORY_VALUES = {category.value for category in OpportunityCategory}
_MAX_MATCHES_PER_KIND = 3
_CORPUS_NOTE = "corpus match — no external link"


def _category(raw: str) -> OpportunityCategory | None:
    return OpportunityCategory(raw) if raw in _CATEGORY_VALUES else None


def _index_candidates(candidates: list[dict]) -> dict[str, dict]:
    return {candidate["id"]: candidate for candidate in candidates if "id" in candidate}


def _build_axes(verdict: dict) -> dict[str, ReportAxis]:
    raw_axes: dict = verdict.get("axes") or {}
    return {
        axis: ReportAxis(score=data.get("score", 0), refs=data.get("refs", []))
        for axis, data in raw_axes.items()
    }


def _all_refs(axes: dict[str, ReportAxis]) -> list[str]:
    return [ref for axis in axes.values() for ref in axis.refs]


def _grounding(candidate: dict) -> Grounding:
    provenance = candidate.get("provenance") or {}
    return Grounding(
        chunk_ids=provenance.get("chunk_ids", []),
        excerpts=provenance.get("excerpts", []),
    )


def _candidate_chunk_ids(candidate: dict) -> set[str]:
    provenance = candidate.get("provenance") or {}
    return {str(chunk_id) for chunk_id in (provenance.get("chunk_ids") or [])}


def _concept_matches(concept: ConceptLandscape, chunk_ids: set[str], target_concept: str) -> bool:
    if chunk_ids and set(concept.chunk_ids) & chunk_ids:
        return True
    target = target_concept.strip().lower()
    return bool(target) and concept.concept.strip().lower() == target


def _matched_concepts(
    candidate: dict, landscape: PriorArtLandscape | None
) -> list[ConceptLandscape]:
    if landscape is None:
        return []
    chunk_ids = _candidate_chunk_ids(candidate)
    target_concept = str(candidate.get("target_concept", ""))
    return [
        concept
        for concept in landscape.concepts
        if _concept_matches(concept, chunk_ids, target_concept)
    ]


def _live_proximity(live: LiveMatch) -> PriorArtMatch:
    return PriorArtMatch(
        reference=live.reference,
        title=live.title,
        url=live.url,
        source=live.source or "live",
        relevance_score=live.relevance_score,
    )


def _corpus_proximity(corpus: CorpusMatch) -> PriorArtMatch:
    return PriorArtMatch(
        reference=corpus.id,
        title=corpus.section_label,
        url="",
        source="corpus",
        relevance_score=corpus.score,
        note=_CORPUS_NOTE,
    )


def _prior_art_proximity(concepts: list[ConceptLandscape]) -> list[PriorArtMatch]:
    lives = [match for concept in concepts for match in concept.live_matches]
    corpora = [match for concept in concepts for match in concept.corpus_matches]
    top_live = sorted(lives, key=lambda match: match.relevance_score, reverse=True)
    top_corpus = sorted(corpora, key=lambda match: match.score, reverse=True)
    return [_live_proximity(live) for live in top_live[:_MAX_MATCHES_PER_KIND]] + [
        _corpus_proximity(corpus) for corpus in top_corpus[:_MAX_MATCHES_PER_KIND]
    ]


def _to_opportunity(
    candidate: dict, verdict: dict, bundle: dict | None, concepts: list[ConceptLandscape]
) -> SeedingOpportunity:
    axes = _build_axes(verdict)
    weighted = float(verdict.get("composite_score", 0.0))
    return SeedingOpportunity(
        id=candidate["id"],
        candidate_id=candidate["id"],
        title=candidate.get("title", ""),
        description=candidate.get("description", ""),
        confidence_score=round(weighted, 1),
        roadmap_alignment=candidate.get("roadmap_alignment", ""),
        category=_category(candidate.get("category", "")),
        innovation_rationale=candidate.get("novelty_delta", ""),
        grounded_in=_grounding(candidate),
        novelty_delta=candidate.get("novelty_delta", ""),
        mechanism=candidate.get("mechanism", ""),
        claim_statement=candidate.get("claim_text", ""),
        round_index=candidate.get("round_index"),
        evidence_bundle_id=(bundle or {}).get("id", ""),
        weighted_score=weighted,
        axes=axes,
        citations=resolve_citations(_all_refs(axes), bundle),
        source_availability=resolve_source_availability(bundle),
        source_status=resolve_source_status(bundle),
        evidence_sources=resolve_evidence_source_views(bundle),
        target_concept=str(candidate.get("target_concept", "")),
        prior_art_proximity=_prior_art_proximity(concepts),
    )


def assemble_opportunities(
    candidates: list[dict],
    verdicts: list[dict],
    evidence_bundles: dict[str, dict],
    landscape: PriorArtLandscape | None = None,
) -> list[SeedingOpportunity]:
    candidate_map = _index_candidates(candidates)
    opportunities: list[SeedingOpportunity] = []
    for verdict in verdicts:
        candidate_id = verdict.get("candidate_id")
        candidate = candidate_map.get(candidate_id)
        if candidate is None:
            continue
        concepts = _matched_concepts(candidate, landscape)
        opportunities.append(
            _to_opportunity(candidate, verdict, evidence_bundles.get(candidate_id), concepts)
        )
    return opportunities


def _whitespace_chunk_ids(landscape: PriorArtLandscape) -> set[str]:
    return {
        str(chunk_id)
        for intersection in landscape.whitespace
        for chunk_id in intersection.chunk_ids
    }


def _opportunity_matches(concept: ConceptLandscape, opportunity: SeedingOpportunity) -> bool:
    chunk_ids = set(opportunity.grounded_in.chunk_ids) if opportunity.grounded_in else set()
    return _concept_matches(concept, chunk_ids, opportunity.target_concept)


def _concept_entry(
    concept: ConceptLandscape, opportunity_ids: list[str], whitespace_chunk_ids: set[str]
) -> ConceptMapEntry:
    return ConceptMapEntry(
        concept=concept.concept,
        density=concept.density,
        corpus_axis=concept.corpus_axis,
        live_axis=concept.live_axis,
        opportunity_ids=opportunity_ids,
        whitespace=bool(set(concept.chunk_ids) & whitespace_chunk_ids),
    )


def build_concept_map(
    opportunities: list[SeedingOpportunity], landscape: PriorArtLandscape | None
) -> list[ConceptMapEntry]:
    if landscape is None:
        return []
    whitespace_chunk_ids = _whitespace_chunk_ids(landscape)
    entries: list[ConceptMapEntry] = []
    for concept in landscape.concepts:
        opportunity_ids = [
            opportunity.id
            for opportunity in opportunities
            if _opportunity_matches(concept, opportunity)
        ]
        if not opportunity_ids:
            continue
        entries.append(_concept_entry(concept, opportunity_ids, whitespace_chunk_ids))
    return entries
