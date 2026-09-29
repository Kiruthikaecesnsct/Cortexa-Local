from dataclasses import dataclass

from scoring.application.dtos.opportunity_detail_response import (
    AxisScoreDto,
    ClaimSeedsDto,
    CommercialPotentialDto,
    EvidenceSourceDto,
    OpportunityDetailDto,
    ProvenanceDto,
    SimilarPatentsDto,
)
from scoring.domain.enums.scoring_axis import ScoringAxis
from scoring.domain.services.axis_reasoning import axis_name_for_frontend, axis_reasoning_for
from scoring.domain.services.recommendation import recommendation_for
from scoring.infrastructure.cosmos.candidate_repository import CosmosCandidateRepository
from scoring.infrastructure.cosmos.evidence_bundle_repository import CosmosEvidenceBundleRepository
from scoring.infrastructure.cosmos.seeding_repository import CosmosClaimSeedRepository
from scoring.infrastructure.cosmos.verdict_repository import CosmosVerdictRepository

TITLE_MAX_LEN = 80

_SOURCE_TYPE_MAP = {
    "PatentApi": "patent_api",
    "SeedCorpus": "vector_corpus",
    "LlmResearch": "llm_deep_research",
}


@dataclass(frozen=True)
class GetVerdictHandlerDeps:
    verdict_repo: CosmosVerdictRepository
    candidate_repo: CosmosCandidateRepository
    evidence_repo: CosmosEvidenceBundleRepository
    seeding_repo: CosmosClaimSeedRepository


class GetVerdictHandler:
    def __init__(self, deps: GetVerdictHandlerDeps) -> None:
        self._deps = deps

    async def handle(self, candidate_id: str) -> OpportunityDetailDto:
        verdict = await self._deps.verdict_repo.get_by_candidate(candidate_id)
        batch_id = verdict.batch_id

        candidate = await self._deps.candidate_repo.get_by_candidate_id(candidate_id, batch_id)
        evidence_bundle = await self._deps.evidence_repo.get_by_candidate_id(candidate_id, batch_id)
        claim_seed_set = await self._deps.seeding_repo.get_by_candidate_id(candidate_id, batch_id)

        title = _derive_title(candidate.claim_text)
        abstract = candidate.problem if candidate.problem else candidate.claim_text

        axis_scores = [
            AxisScoreDto(
                axis=axis_name_for_frontend(axis),
                score=score.score,
                reasoning=axis_reasoning_for(axis, score.score, score.refs),
                citations=score.refs,
            )
            for axis, score in verdict.axes.items()
        ]

        evidence_sources = _build_evidence_sources(evidence_bundle)

        provenance = ProvenanceDto(
            source_document_id=candidate.document_id,
            excerpt_text=candidate.source_span.get("excerpt", ""),
            source_filename=None,
            page_number=candidate.source_span.get("page_number"),
            span_start=candidate.source_span.get("span_start"),
            span_end=candidate.source_span.get("span_end"),
        )

        similar_patents = None
        if evidence_bundle:
            similar_patents = SimilarPatentsDto(
                count=len(evidence_bundle.hits),
                source_count=len(evidence_bundle.sources_used),
            )

        claim_seeds = None
        if claim_seed_set:
            claim_seeds = ClaimSeedsDto(
                count=len(claim_seed_set.independent_claims),
                total=len(claim_seed_set.independent_claims) + len(claim_seed_set.dependent_claims),
            )

        commercial_potential = None
        if ScoringAxis.Commercial in verdict.axes and candidate.tech_field:
            commercial_score = verdict.axes[ScoringAxis.Commercial].score
            level = (
                "high"
                if commercial_score >= 70.0
                else "medium"
                if commercial_score >= 40.0
                else "low"
            )
            commercial_potential = CommercialPotentialDto(
                level=level,
                industry=candidate.tech_field,
            )

        return OpportunityDetailDto(
            candidate_id=candidate_id,
            title=title,
            abstract=abstract,
            claim_draft=verdict.drafted_claim or "",
            overall_score=verdict.composite_score,
            recommendation=recommendation_for(verdict.composite_score),
            axis_scores=axis_scores,
            evidence_sources=evidence_sources,
            provenance=provenance,
            similar_patents=similar_patents,
            claim_seeds=claim_seeds,
            commercial_potential=commercial_potential,
        )


def _derive_title(claim_text: str) -> str:
    first_sentence = claim_text.split(".")[0].strip()
    if len(first_sentence) <= TITLE_MAX_LEN:
        return first_sentence
    return claim_text[:TITLE_MAX_LEN].strip()


def _build_evidence_sources(evidence_bundle) -> list[EvidenceSourceDto]:
    if not evidence_bundle:
        return []

    sources_data = {}
    for source_type_backend, source_type_frontend in _SOURCE_TYPE_MAP.items():
        available = evidence_bundle.source_flags.get(source_type_backend, False)
        hits_for_source = [
            hit for hit in evidence_bundle.hits if source_type_backend in hit.get("sources", [])
        ]
        confidence = (
            sum(hit.get("similarity", 0.0) for hit in hits_for_source) / len(hits_for_source)
            if hits_for_source
            else 0.0
        )
        citations = [hit.get("citation", "") for hit in hits_for_source if hit.get("citation")]

        sources_data[source_type_frontend] = EvidenceSourceDto(
            source_type=source_type_frontend,
            available=available,
            confidence=confidence,
            citations=citations,
            summary=f"{len(hits_for_source)} hit(s) from {source_type_frontend}",
        )

    return list(sources_data.values())
