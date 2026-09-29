from dataclasses import dataclass

from seeding.application.dtos.generate_claim_seeds_request import GenerateClaimSeedsRequest
from seeding.application.dtos.generate_claim_seeds_response import GenerateClaimSeedsResponse
from seeding.application.parsing.claim_seed_parser import parse_claim_seed_set
from seeding.application.parsing.json_extractor import extract_json
from seeding.application.prompt.claim_seed_prompt_builder import build_prompt
from seeding.domain.models.evidence_bundle import EvidenceBundle
from seeding.domain.models.idf_draft import IdfDraft
from seeding.domain.models.scored_candidate import ScoredCandidate
from seeding.domain.ports.model_router_port import ModelRouterPort
from seeding.domain.services.claim_seed_generator import validate_inputs, validate_seed_counts


@dataclass
class GenerateClaimSeedsDeps:
    client: ModelRouterPort


async def handle(
    deps: GenerateClaimSeedsDeps, request: GenerateClaimSeedsRequest
) -> GenerateClaimSeedsResponse:
    candidate = ScoredCandidate(
        candidate_id=request.candidate_id,
        batch_id=request.batch_id,
        job_id=request.job_id,
        document_id=request.document_id,
        axes=request.axes,
    )
    bundle = EvidenceBundle(
        id=request.bundle_id,
        batch_id=request.batch_id,
        job_id=request.job_id,
        candidate_id=request.candidate_id,
        document_id=request.document_id,
        hits=request.hits,
        source_flags=request.source_flags,
    )
    idf_draft = IdfDraft(
        candidate_id=request.candidate_id,
        batch_id=request.batch_id,
        job_id=request.job_id,
        document_id=request.document_id,
        abstract=request.idf_abstract,
        background=request.idf_background,
        summary=request.idf_summary,
        core_differentiating_feature=request.idf_core_differentiating_feature,
    )
    validate_inputs(candidate, idf_draft, bundle)
    prompt, evidence_refs = build_prompt(candidate, idf_draft, bundle)
    result = await deps.client.complete(prompt, evidence_refs, model=request.ai_model)
    raw = extract_json(result.content)
    seed_set = parse_claim_seed_set(raw, candidate, evidence_refs)
    validate_seed_counts(seed_set)
    return GenerateClaimSeedsResponse.from_seed_set(seed_set)
