from seeding.domain.errors.seeding_errors import IdfParseError
from seeding.domain.models.idf_draft import IdfDraft
from seeding.domain.models.idf_section import IdfSection
from seeding.domain.models.scored_candidate import ScoredCandidate
from seeding.domain.services.idf_generator import validate_abstract_length

_REQUIRED_KEYS = ("abstract", "background", "summary", "core_differentiating_feature")


def _validate_citation(citation: str, evidence_refs: list[str], key: str) -> None:
    if citation not in evidence_refs:
        raise IdfParseError(f"citation '{citation}' in section '{key}' not found in evidence refs")


def _parse_section(raw: dict, key: str, evidence_refs: list[str]) -> IdfSection:
    raw_section = raw.get(key)
    if raw_section is None:
        raise IdfParseError(f"section '{key}' missing from response")
    citations = raw_section.get("citations", [])
    if not citations:
        raise IdfParseError(f"section '{key}' has no citations")
    for citation in citations:
        _validate_citation(citation, evidence_refs, key)
    text = raw_section.get("text", "")
    if key == "abstract":
        validate_abstract_length(text)
    return IdfSection(text=text, citations=citations)


def parse_idf_draft(raw: dict, candidate: ScoredCandidate, evidence_refs: list[str]) -> IdfDraft:
    sections = {key: _parse_section(raw, key, evidence_refs) for key in _REQUIRED_KEYS}
    return IdfDraft(
        candidate_id=candidate.candidate_id,
        batch_id=candidate.batch_id,
        job_id=candidate.job_id,
        document_id=candidate.document_id,
        abstract=sections["abstract"],
        background=sections["background"],
        summary=sections["summary"],
        core_differentiating_feature=sections["core_differentiating_feature"],
    )
