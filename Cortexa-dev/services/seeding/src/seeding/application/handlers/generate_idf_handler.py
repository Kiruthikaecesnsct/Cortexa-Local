from dataclasses import dataclass

from seeding.application.dtos.generate_idf_request import GenerateIdfRequest
from seeding.application.dtos.generate_idf_response import GenerateIdfResponse
from seeding.application.parsing.idf_parser import parse_idf_draft
from seeding.application.parsing.json_extractor import extract_json
from seeding.application.prompt.idf_prompt_builder import build_prompt
from seeding.domain.models.evidence_bundle import EvidenceBundle
from seeding.domain.models.opportunity_map import OpportunityMap
from seeding.domain.models.scored_candidate import ScoredCandidate
from seeding.domain.ports.model_router_port import ModelRouterPort
from seeding.domain.services.idf_generator import validate_inputs


@dataclass
class GenerateIdfDeps:
    client: ModelRouterPort


async def handle(deps: GenerateIdfDeps, request: GenerateIdfRequest) -> GenerateIdfResponse:
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
    opportunity_map = OpportunityMap(
        candidate_id=request.candidate_id,
        batch_id=request.batch_id,
        document_id=request.document_id,
        opportunities=request.opportunities,
    )
    validate_inputs(candidate, opportunity_map, bundle)
    prompt, evidence_refs = build_prompt(candidate, opportunity_map, bundle)
    result = await deps.client.complete(prompt, evidence_refs, model=request.ai_model)
    raw = extract_json(result.content)
    draft = parse_idf_draft(raw, candidate, evidence_refs)
    return GenerateIdfResponse.from_draft(draft)
