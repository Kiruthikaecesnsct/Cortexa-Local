from dataclasses import dataclass

from seeding.application.dtos.generate_map_request import GenerateMapRequest
from seeding.application.dtos.generate_map_response import GenerateMapResponse
from seeding.application.parsing.json_extractor import extract_json
from seeding.application.parsing.opportunity_parser import parse_opportunity_map
from seeding.application.prompt.opportunity_prompt_builder import build_prompt
from seeding.domain.models.evidence_bundle import EvidenceBundle
from seeding.domain.models.scored_candidate import ScoredCandidate
from seeding.domain.ports.model_router_port import ModelRouterPort
from seeding.domain.services.opportunity_map_generator import validate_inputs


@dataclass
class GenerateMapDeps:
    client: ModelRouterPort


async def handle(deps: GenerateMapDeps, request: GenerateMapRequest) -> GenerateMapResponse:
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
    validate_inputs(candidate, bundle)
    prompt, evidence_refs = build_prompt(candidate, bundle)
    result = await deps.client.complete(prompt, evidence_refs, model=request.ai_model)
    raw = extract_json(result.content)
    opp_map = parse_opportunity_map(raw, candidate, evidence_refs)
    return GenerateMapResponse.from_map(opp_map)
