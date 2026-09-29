import uuid
from dataclasses import dataclass
from datetime import UTC, datetime

from seeding.application.dtos.generate_lattice_request import GenerateLatticeRequest
from seeding.application.dtos.generate_lattice_response import GenerateLatticeResponse
from seeding.application.parsing.json_extractor import extract_json
from seeding.application.parsing.lattice_parser import parse_lattice
from seeding.application.prompt.lattice_prompt_builder import build_prompt
from seeding.application.rendering.lattice_diagram import render_lattice_text
from seeding.domain.models.evidence_bundle import EvidenceBundle
from seeding.domain.models.idf_draft import IdfDraft
from seeding.domain.models.invention_lattice import InventionLattice
from seeding.domain.models.scored_candidate import ScoredCandidate
from seeding.domain.models.seeding_result import SeedingOpportunity, SeedingResult
from seeding.domain.ports.event_publisher_port import EventPublisherPort
from seeding.domain.ports.model_router_port import ModelRouterPort
from seeding.domain.services.confidence import calculate_confidence_score
from seeding.domain.services.invention_lattice_generator import (
    validate_inputs,
    validate_lattice,
)
from seeding.infrastructure.config.settings import SeedingSettings


@dataclass
class GenerateLatticeDeps:
    client: ModelRouterPort
    publisher: EventPublisherPort
    settings: SeedingSettings
    seeding_repository: object


def _build_candidate(request: GenerateLatticeRequest) -> ScoredCandidate:
    return ScoredCandidate(
        candidate_id=request.candidate_id,
        batch_id=request.batch_id,
        job_id=request.job_id,
        document_id=request.document_id,
        axes=request.axes,
    )


def _build_bundle(request: GenerateLatticeRequest) -> EvidenceBundle:
    return EvidenceBundle(
        id=request.bundle_id,
        batch_id=request.batch_id,
        job_id=request.job_id,
        candidate_id=request.candidate_id,
        document_id=request.document_id,
        hits=request.hits,
        source_flags=request.source_flags,
    )


def _build_idf_draft(request: GenerateLatticeRequest) -> IdfDraft:
    return IdfDraft(
        candidate_id=request.candidate_id,
        batch_id=request.batch_id,
        job_id=request.job_id,
        document_id=request.document_id,
        abstract=request.idf_abstract,
        background=request.idf_background,
        summary=request.idf_summary,
        core_differentiating_feature=request.idf_core_differentiating_feature,
    )


def _lattice_to_opportunities(
    lattice: InventionLattice, candidate: ScoredCandidate
) -> list[SeedingOpportunity]:
    opportunities = []
    confidence = calculate_confidence_score(candidate.axes)

    core_opp = SeedingOpportunity(
        id=f"{lattice.candidate_id}-core",
        title=lattice.core.title,
        description=lattice.core.description,
        confidence_score=confidence,
        roadmap_alignment="Core",
    )
    opportunities.append(core_opp)

    for idx, continuation in enumerate(lattice.continuations):
        opp = SeedingOpportunity(
            id=f"{lattice.candidate_id}-continuation-{idx}",
            title=continuation.title,
            description=continuation.description,
            confidence_score=confidence,
            roadmap_alignment="Continuation",
        )
        opportunities.append(opp)

    for idx, platform in enumerate(lattice.platform):
        opp = SeedingOpportunity(
            id=f"{lattice.candidate_id}-platform-{idx}",
            title=platform.title,
            description=platform.description,
            confidence_score=confidence,
            roadmap_alignment="Platform",
        )
        opportunities.append(opp)

    for idx, system in enumerate(lattice.system):
        opp = SeedingOpportunity(
            id=f"{lattice.candidate_id}-system-{idx}",
            title=system.title,
            description=system.description,
            confidence_score=confidence,
            roadmap_alignment="System",
        )
        opportunities.append(opp)

    return opportunities


async def _publish_completion(
    deps: GenerateLatticeDeps, batch_id: str, document_id: str, report_id: str
) -> None:
    event = {
        "event_id": str(uuid.uuid4()),
        "schema_version": "1.0",
        "event_type": "engine.completed",
        "batch_id": batch_id,
        "document_id": document_id,
        "occurred_at": datetime.now(UTC).isoformat(),
        "payload": {
            "document_id": document_id,
            "engine": deps.settings.engine_name,
            "report_id": report_id,
        },
    }
    await deps.publisher.publish(deps.settings.engine_completed_topic, event, session_id=batch_id)


async def handle(
    deps: GenerateLatticeDeps, request: GenerateLatticeRequest
) -> GenerateLatticeResponse:
    candidate = _build_candidate(request)
    bundle = _build_bundle(request)
    idf_draft = _build_idf_draft(request)

    validate_inputs(candidate, idf_draft, bundle)
    prompt, evidence_refs = build_prompt(candidate, idf_draft, bundle)
    result = await deps.client.complete(prompt, evidence_refs, model=request.ai_model)
    raw = extract_json(result.content)
    lattice = parse_lattice(raw, candidate, evidence_refs)
    validate_lattice(lattice)

    opportunities = _lattice_to_opportunities(lattice, candidate)
    seeding_result = SeedingResult(
        id=str(uuid.uuid4()), batch_id=lattice.batch_id, opportunities=opportunities
    )
    await deps.seeding_repository.save(seeding_result)

    diagram_text = render_lattice_text(lattice)
    report_id = str(uuid.uuid4())
    await _publish_completion(deps, lattice.batch_id, lattice.document_id, report_id)

    return GenerateLatticeResponse.from_lattice(lattice, diagram_text, report_id)
