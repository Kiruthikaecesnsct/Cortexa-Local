from dataclasses import dataclass

from scoring.domain.enums.evidence_source import EvidenceSource
from scoring.domain.errors.scoring_errors import UngroundedVerdictError
from scoring.domain.models.evidence_bundle import EvidenceBundle
from scoring.domain.models.evidence_hit import EvidenceHit

_SOURCE_ORDER = [EvidenceSource.PatentApi, EvidenceSource.SeedCorpus, EvidenceSource.LlmResearch]

_TRUNCATION_SUFFIX = "…[truncated]"


@dataclass(frozen=True)
class HitRenderConfig:
    max_claim_chars_per_hit: int = 700
    max_abstract_chars_per_hit: int = 300
    claims_per_hit: int = 1


def _sanitize(text: str) -> str:
    return text.replace("==", "--").strip()


def _truncate(text: str, limit: int) -> str:
    if len(text) <= limit:
        return text
    cut_point = text.rfind(" ", 0, limit + 1)
    if cut_point <= 0:
        cut_point = limit
    return text[:cut_point].rstrip() + _TRUNCATION_SUFFIX


_SECTION_LABEL = {
    EvidenceSource.PatentApi: "PATENT API EVIDENCE",
    EvidenceSource.SeedCorpus: "SEED CORPUS EVIDENCE",
    EvidenceSource.LlmResearch: "LLM RESEARCH EVIDENCE",
}


def _resolve_source_status(source: EvidenceSource, bundle: EvidenceBundle) -> str:
    flag = bundle.source_flags.get(source)
    if flag is False or flag is None:
        return "unavailable"
    if source not in bundle.sources_used:
        return "fallback"
    return "available"


def _render_hit_abstract(hit: EvidenceHit, cfg: HitRenderConfig) -> str | None:
    abstract = _sanitize(hit.abstract)
    if not abstract:
        return None
    return f"      abstract: {_truncate(abstract, cfg.max_abstract_chars_per_hit)}"


def _render_hit_claims(hit: EvidenceHit, cfg: HitRenderConfig) -> list[str]:
    lines: list[str] = []
    for i, raw_claim in enumerate(hit.claims[: cfg.claims_per_hit], start=1):
        claim = _sanitize(raw_claim)
        if claim:
            lines.append(f"      claim {i}: {_truncate(claim, cfg.max_claim_chars_per_hit)}")
    return lines


def _render_hit(ref: str, hit: EvidenceHit, cfg: HitRenderConfig) -> str:
    patent_prefix = f"patent:{hit.patent_id} | " if hit.patent_id is not None else ""
    tail = f"{hit.title} | {hit.citation} | url: {hit.url} | hash: {hit.content_hash}"
    lines = [f"[{ref}] {patent_prefix}{tail}"]
    abstract_line = _render_hit_abstract(hit, cfg)
    if abstract_line is not None:
        lines.append(abstract_line)
    lines.extend(_render_hit_claims(hit, cfg))
    return "\n".join(lines)


def _validate_bundle(bundle: EvidenceBundle) -> None:
    if not bundle.hits:
        raise UngroundedVerdictError("evidence bundle contains no hits")

    all_unavailable = all(
        bundle.source_flags.get(source) is False or bundle.source_flags.get(source) is None
        for source in _SOURCE_ORDER
    )
    if all_unavailable:
        raise UngroundedVerdictError("all evidence sources are unavailable")


def _render_header(safe_desc: str) -> list[str]:
    return [
        "You are a patent examiner performing a 5-axis patentability assessment.",
        "Score ONLY based on the evidence provided below. "
        "Every axis score MUST cite at least one evidence reference.",
        "",
        "== CANDIDATE ==",
        safe_desc,
    ]


def _render_source_sections(
    sorted_hits: list[EvidenceHit],
    ref_map: dict[int, str],
    bundle: EvidenceBundle,
    cfg: HitRenderConfig,
) -> list[str]:
    sections: list[str] = []
    for source in _SOURCE_ORDER:
        status = _resolve_source_status(source, bundle)
        label = _SECTION_LABEL[source]
        sections.append("")
        sections.append(f"== {label} [status: {status}] ==")
        source_hits = [
            (ref_map[i], hit) for i, hit in enumerate(sorted_hits) if source in hit.sources
        ]
        if source_hits:
            for ref, hit in source_hits:
                sections.append(_render_hit(ref, hit, cfg))
        else:
            sections.append("(no hits)")
    return sections


def _render_footer() -> list[str]:
    return [
        "",
        "== SCORING INSTRUCTIONS ==",
        "Score each axis 0–100. For each axis cite the evidence references that support your score.",  # noqa: E501
        "Axes: Novelty, Inventiveness, Commercial, Strategic, Patentability",
        "",
        "Return ONLY a JSON object:",
        '{"Novelty": {"score": 0, "refs": ["E1"]}, ...}',
    ]


def build_grounded_prompt(
    candidate_description: str,
    bundle: EvidenceBundle,
    render_config: HitRenderConfig = HitRenderConfig(),
) -> str:
    _validate_bundle(bundle)
    safe_desc = _sanitize(candidate_description)
    # Mirrored in harvesting's evidence_citation_resolver.py — keep both sort keys identical.
    sorted_hits = sorted(bundle.hits, key=lambda h: (h.patent_id or "", h.content_hash))
    ref_map: dict[int, str] = {i: f"E{i + 1}" for i in range(len(sorted_hits))}
    sections = (
        _render_header(safe_desc)
        + _render_source_sections(sorted_hits, ref_map, bundle, render_config)
        + _render_footer()
    )
    return "\n".join(sections)
