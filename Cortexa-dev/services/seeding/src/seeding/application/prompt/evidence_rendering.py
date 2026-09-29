from seeding.domain.enums.scoring_axis import ScoringAxis
from seeding.domain.models.evidence_bundle import EvidenceHit
from seeding.domain.models.scored_candidate import ScoredCandidate


def format_ref(hit: EvidenceHit) -> str:
    return f"[{hit.source_id}:{hit.start}-{hit.end}]"


def render_axes(candidate: ScoredCandidate) -> list[str]:
    lines = ["", "== AXIS SCORES =="]
    for axis in ScoringAxis:
        ax = candidate.axes[axis]
        lines.append(f"{axis}: {ax.score}/100  refs={ax.refs}")
    return lines


def render_evidence(hits: list[EvidenceHit]) -> list[str]:
    lines = ["", "== EVIDENCE HITS =="]
    for hit in hits:
        ref = format_ref(hit)
        lines.append(f"{ref} {hit.text[:300]}")
    return lines
