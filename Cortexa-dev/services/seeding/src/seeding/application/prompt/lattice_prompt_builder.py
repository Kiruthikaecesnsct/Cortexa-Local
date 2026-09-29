from seeding.application.prompt.evidence_rendering import (
    format_ref,
    render_axes,
    render_evidence,
)
from seeding.domain.models.evidence_bundle import EvidenceBundle
from seeding.domain.models.idf_draft import IdfDraft
from seeding.domain.models.scored_candidate import ScoredCandidate

_ENTRY_FIELDS = (
    '  "title": string,',
    '  "description": string,',
    '  "scope": string,',
    '  "filing_strategy_note": string,',
    '  "evidence_refs": list of evidence reference strings drawn verbatim from the list above.',
)

_FOOTER = [
    "",
    "== OUTPUT INSTRUCTIONS ==",
    "Return ONLY a JSON object with exactly 4 keys: core, continuations, platform, system.",
    "core is a single object with:",
    *_ENTRY_FIELDS,
    "continuations, platform, and system are each a list of at least 2 objects, each with:",
    *_ENTRY_FIELDS,
    "core is the central invention. continuations are follow-on filing opportunities,",
    "platform entries broaden the invention into a platform play, and system entries",
    "place the invention inside a larger system context.",
    "Every entry must cite at least one evidence reference from the list above.",
    "This is a draft invention lattice for a patent attorney to refine — not final,",
    "filing-ready material. Do not fabricate evidence references not present above.",
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
        "You are a patent attorney's assistant drafting an invention lattice: a core",
        "invention plus continuation, platform, and system-level filing opportunities an",
        "attorney will refine into final, filing-ready material. Ground every entry",
        "strictly in the evidence below.",
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
