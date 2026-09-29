from seeding.application.prompt.evidence_rendering import (
    format_ref,
    render_axes,
    render_evidence,
)
from seeding.domain.models.evidence_bundle import EvidenceBundle
from seeding.domain.models.scored_candidate import ScoredCandidate

_FOOTER = [
    "",
    "== OUTPUT INSTRUCTIONS ==",
    "Return ONLY a JSON object with exactly 4 keys: Whitespace, Defensive, Adjacent, Continuation.",
    "Each key maps to a list of opportunity objects.",
    "Each opportunity object must have:",
    '  "description": string,',
    '  "justification": string,',
    '  "citations": list of evidence reference strings (e.g. ["[src1:0-100]"])',
    "Every opportunity must cite at least one evidence reference from the list above.",
    "Each category must contain at least one opportunity.",
]


def build_prompt(candidate: ScoredCandidate, bundle: EvidenceBundle) -> tuple[str, list[str]]:
    evidence_refs = [format_ref(h) for h in bundle.hits]
    header = [
        "You are a patent strategy analyst identifying patent landscape gaps.",
        "Analyze the scored candidate and evidence to identify opportunities in 4 categories:",
        "Whitespace (unclaimed territory), Defensive (protect existing position),",
        "Adjacent (related technology areas), Continuation (extensions of existing claims).",
        "",
        f"== CANDIDATE ID: {candidate.candidate_id} ==",
        f"Batch: {candidate.batch_id} | Document: {candidate.document_id}",
    ]
    sections = header + render_axes(candidate) + render_evidence(bundle.hits) + _FOOTER
    return "\n".join(sections), evidence_refs
