from collections.abc import Iterable
from dataclasses import dataclass, field

from seeding.domain.models.citation import Citation

_SOURCE_TYPE_MAP = {
    "PatentApi": "patent_api",
    "SeedCorpus": "vector_corpus",
    "LlmResearch": "llm_deep_research",
}
_SOURCE_PRIORITY = ["PatentApi", "SeedCorpus", "LlmResearch"]
_UNKNOWN_SOURCE_TYPE = "unknown"
_REF_PREFIX = "E"
_DEFAULT_SOURCE_STATUS = "empty"


def resolve_citations(refs: Iterable[str], evidence_bundle: dict | None) -> list[Citation]:
    hits = _bundle_hits(evidence_bundle)
    if not hits:
        return []
    sorted_hits = _sort_hits(hits)
    citations: list[Citation] = []
    for ref in _unique_refs(refs):
        hit = _hit_for_ref(ref, sorted_hits)
        if hit is not None:
            citations.append(_to_citation(ref, hit))
    return citations


def resolve_source_availability(evidence_bundle: dict | None) -> dict[str, bool]:
    source_flags = (evidence_bundle or {}).get("source_flags") or {}
    availability: dict[str, bool] = {}
    for backend_key, frontend_key in _SOURCE_TYPE_MAP.items():
        availability[frontend_key] = bool(source_flags.get(backend_key, False))
    return availability


def resolve_source_status(evidence_bundle: dict | None) -> dict[str, str]:
    source_status = (evidence_bundle or {}).get("source_status") or {}
    status: dict[str, str] = {}
    for backend_key, frontend_key in _SOURCE_TYPE_MAP.items():
        value = source_status.get(backend_key)
        status[frontend_key] = str(value) if value else _DEFAULT_SOURCE_STATUS
    return status


def _bundle_hits(evidence_bundle: dict | None) -> list[dict]:
    if not evidence_bundle:
        return []
    return evidence_bundle.get("hits") or []


def _sort_hits(hits: list[dict]) -> list[dict]:
    return sorted(hits, key=lambda h: (h.get("patent_id") or "", h.get("content_hash", "")))


def _unique_refs(refs: Iterable[str]) -> list[str]:
    seen: dict[str, None] = {}
    for ref in refs:
        seen.setdefault(ref, None)
    return list(seen)


def _hit_for_ref(ref: str, sorted_hits: list[dict]) -> dict | None:
    index = _ref_to_index(ref)
    if index is None or not (0 <= index < len(sorted_hits)):
        return None
    return sorted_hits[index]


def _ref_to_index(ref: str) -> int | None:
    if not ref.startswith(_REF_PREFIX):
        return None
    number = ref[len(_REF_PREFIX) :]
    if not number.isdigit():
        return None
    return int(number) - 1


def _to_citation(ref: str, hit: dict) -> Citation:
    return Citation(
        ref=ref,
        source_type=_primary_source_type(hit),
        title=hit.get("title", ""),
        patent_id=hit.get("patent_id"),
        url=hit.get("url", ""),
        similarity=float(hit.get("similarity", 0.0)),
    )


def _primary_source_type(hit: dict) -> str:
    sources = hit.get("sources") or []
    for source in _SOURCE_PRIORITY:
        if source in sources:
            return _SOURCE_TYPE_MAP[source]
    return _UNKNOWN_SOURCE_TYPE


_PATENT_API_VIEW = "patent_api"
_LLM_DEEP_RESEARCH_VIEW = "llm_deep_research"


@dataclass(frozen=True)
class _SourceFlags:
    available: bool
    status: str


@dataclass(frozen=True)
class _BundleContext:
    llm_research: dict | None = None
    degraded: bool = False
    degraded_sources: list[str] = field(default_factory=list)


def resolve_evidence_source_views(evidence_bundle: dict | None) -> list[dict]:
    """Build one card view per frontend source, carrying ALL of its own hits.

    Unlike resolve_citations (which resolves only the E-refs the scoring/
    ideation LLM cited in an axis), this groups every bundle hit under EVERY
    source in its `sources` set — a multi-source hit appears on every card it
    belongs to. Availability/status mirror resolve_source_availability /
    resolve_source_status; confidence is the mean similarity of a source's own
    hits, except the LLM card, which uses llm_research.confidence when present.
    """
    hits = _sort_hits(_bundle_hits(evidence_bundle))
    grouped = _group_hits_by_source(hits)
    availability = resolve_source_availability(evidence_bundle)
    statuses = resolve_source_status(evidence_bundle)
    context = _bundle_context(evidence_bundle)
    return [
        _build_source_view(
            frontend_key,
            grouped.get(frontend_key, []),
            _SourceFlags(
                availability.get(frontend_key, False),
                statuses.get(frontend_key, _DEFAULT_SOURCE_STATUS),
            ),
            context,
        )
        for frontend_key in _SOURCE_TYPE_MAP.values()
    ]


def _bundle_context(evidence_bundle: dict | None) -> _BundleContext:
    bundle = evidence_bundle or {}
    return _BundleContext(
        llm_research=bundle.get("llm_research"),
        degraded=bool(bundle.get("degraded", False)),
        degraded_sources=list(bundle.get("degraded_sources") or []),
    )


def _group_hits_by_source(hits: list[dict]) -> dict[str, list[dict]]:
    grouped: dict[str, list[dict]] = {key: [] for key in _SOURCE_TYPE_MAP.values()}
    for hit in hits:
        for backend_source in hit.get("sources") or []:
            frontend_key = _SOURCE_TYPE_MAP.get(backend_source)
            if frontend_key is not None:
                grouped[frontend_key].append(_to_hit_view(hit))
    return grouped


def _to_hit_view(hit: dict) -> dict:
    return {
        "title": hit.get("title", ""),
        "patent_id": hit.get("patent_id"),
        "url": hit.get("url", ""),
        "similarity": float(hit.get("similarity", 0.0)),
        "jurisdiction": hit.get("jurisdiction", ""),
    }


def _mean_similarity(hits: list[dict]) -> float:
    if not hits:
        return 0.0
    return sum(hit["similarity"] for hit in hits) / len(hits)


def _source_confidence(frontend_key: str, hits: list[dict], context: _BundleContext) -> float:
    if frontend_key == _LLM_DEEP_RESEARCH_VIEW and context.llm_research:
        return float(context.llm_research.get("confidence", 0.0))
    return _mean_similarity(hits)


def _source_reasoning(frontend_key: str, context: _BundleContext) -> list[str]:
    if frontend_key != _LLM_DEEP_RESEARCH_VIEW or not context.llm_research:
        return []
    return list(context.llm_research.get("findings") or [])


def _build_source_view(
    frontend_key: str,
    hits: list[dict],
    flags: _SourceFlags,
    context: _BundleContext,
) -> dict:
    view = {
        "source_type": frontend_key,
        "available": flags.available,
        "status": flags.status,
        "hits": hits,
        "confidence": _source_confidence(frontend_key, hits, context),
        "reasoning": _source_reasoning(frontend_key, context),
    }
    if frontend_key == _PATENT_API_VIEW:
        view["degraded"] = context.degraded
        view["degraded_sources"] = context.degraded_sources
    return view
