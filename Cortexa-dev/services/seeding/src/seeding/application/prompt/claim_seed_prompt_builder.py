from seeding.application.prompt.evidence_rendering import (
    format_ref,
    render_axes,
    render_evidence,
)
from seeding.domain.models.evidence_bundle import EvidenceBundle
from seeding.domain.models.idf_draft import IdfDraft
from seeding.domain.models.scored_candidate import ScoredCandidate

_FOOTER = [
    "",
    "== OUTPUT INSTRUCTIONS ==",
    "Return ONLY a JSON object with exactly 2 keys: independent_claims, dependent_claims.",
    "independent_claims is a list of at least 1 object, each with:",
    '  "claim_type": "method" or "apparatus",',
    '  "preamble": string,',
    '  "recitations": list of strings,',
    '  "limitations": list of objects with "text" (string) and "evidence_refs"',
    "    (list of evidence reference strings drawn verbatim from the list above).",
    "dependent_claims is a list of at least 2 objects, each with:",
    '  "parent_index": integer index into independent_claims,',
    '  "added_limitations": list of objects with "text" and "evidence_refs"',
    "    (same verbatim rule as above).",
    "Every limitation must cite at least one evidence reference from the list above.",
    "These are draft claim seed templates for a patent attorney to refine — not final,",
    "filing-ready claims. Do not fabricate evidence references not present above.",
]


def _render_idf_context(idf_draft: IdfDraft) -> list[str]:
    return [
        "",
        "== IDF CONTEXT ==",
        f"Abstract: {idf_draft.abstract.text}",
        f"Summary: {idf_draft.summary.text}",
        f"Core Differentiating Feature: {idf_draft.core_differentiating_feature.text}",
    ]


def build_prompt(
    candidate: ScoredCandidate, idf_draft: IdfDraft, bundle: EvidenceBundle
) -> tuple[str, list[str]]:
    evidence_refs = [format_ref(h) for h in bundle.hits]
    header = [
        "You are a patent attorney's assistant drafting claim seed templates: an",
        "independent claim and supporting dependent claims an attorney will refine into",
        "final, filing-ready claims. Ground every limitation strictly in the evidence",
        "below.",
        "",
        f"== CANDIDATE ID: {candidate.candidate_id} ==",
        f"Batch: {candidate.batch_id} | Document: {candidate.document_id}",
    ]
    sections = (
        header
        + render_axes(candidate)
        + _render_idf_context(idf_draft)
        + render_evidence(bundle.hits)
        + _FOOTER
    )
    return "\n".join(sections), evidence_refs
