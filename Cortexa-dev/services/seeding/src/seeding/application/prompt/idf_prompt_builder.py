from seeding.application.prompt.evidence_rendering import (
    format_ref,
    render_axes,
    render_evidence,
)
from seeding.domain.enums.scoring_axis import ScoringAxis
from seeding.domain.models.evidence_bundle import EvidenceBundle
from seeding.domain.models.opportunity_map import OpportunityMap
from seeding.domain.models.scored_candidate import ScoredCandidate

_FOOTER = [
    "",
    "== OUTPUT INSTRUCTIONS ==",
    "Return ONLY a JSON object with exactly 4 keys: abstract, background, summary,",
    "core_differentiating_feature.",
    "Each key maps to an object with:",
    '  "text": string,',
    '  "citations": list of evidence reference strings (e.g. ["[src1:0-100]"])',
    "Every section must cite at least one evidence reference from the list above.",
    "The 'abstract' text must not exceed 250 words.",
    "The 'core_differentiating_feature' text must explain why the feature is non-obvious,",
    f"referencing the {ScoringAxis.Novelty} and {ScoringAxis.Feasibility} axis scores above.",
]


def _render_opportunities(opportunity_map: OpportunityMap) -> list[str]:
    lines = ["", "== OPPORTUNITY MAP =="]
    for category, opportunities in opportunity_map.opportunities.items():
        lines.append(f"-- {category} --")
        for opp in opportunities:
            lines.append(f"  {opp.description} ({opp.justification})")
    return lines


def build_prompt(
    candidate: ScoredCandidate, opportunity_map: OpportunityMap, bundle: EvidenceBundle
) -> tuple[str, list[str]]:
    evidence_refs = [format_ref(h) for h in bundle.hits]
    header = [
        "You are a patent attorney's assistant drafting an Independent Dominating Feature",
        "(IDF) statement: an initial draft an attorney will refine into a full patent",
        "application. Produce an abstract, background, summary, and the core",
        "differentiating feature, grounded strictly in the evidence below.",
        "",
        f"== CANDIDATE ID: {candidate.candidate_id} ==",
        f"Batch: {candidate.batch_id} | Document: {candidate.document_id}",
    ]
    sections = (
        header
        + render_axes(candidate)
        + _render_opportunities(opportunity_map)
        + render_evidence(bundle.hits)
        + _FOOTER
    )
    return "\n".join(sections), evidence_refs
