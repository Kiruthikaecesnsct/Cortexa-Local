"""Resolve axis evidence refs (e.g. "E6") into rich Citation payloads.

Pure domain service — no I/O. It reproduces, byte-for-byte, the ref->hit
indexing that the scoring service uses so that a ref like "E6" resolves to the
same hit on both sides. The scoring source of truth lives in TWO places and
MUST stay in sync with this module:

  - services/scoring/src/scoring/application/prompt/grounded_prompt_builder.py
    (lines 141-142: `sorted(bundle.hits, key=lambda h: (h.patent_id or "",
    h.content_hash))` then `E{i + 1}`)
  - services/scoring/src/scoring/application/parsing/five_axis_parser.py
    (lines 16-17: identical sort key + `E{i + 1}` ref set)

Provenance mirrors scoring's ProvenanceDto construction in
services/scoring/src/scoring/application/handlers/get_verdict_handler.py
(lines 64-75), built from the candidate's `source_span` + `document_id`.

Public API (wired in by the harvesting candidate assembler):

    resolve_citations(axes: Iterable[AxisScore],
                      evidence_bundle: dict | None) -> list[Citation]
    resolve_provenance_links(candidate: dict) -> list[ProvenanceLink]
    resolve_source_availability(evidence_bundle: dict | None) -> dict[str, bool]
    resolve_source_status(evidence_bundle: dict | None) -> dict[str, str]
    resolve_evidence_source_views(evidence_bundle: dict | None) -> list[dict]

All degrade gracefully: a missing/None bundle or unresolvable ref yields fewer
(or zero) citations instead of raising. A candidate with neither a document id
nor a source span yields zero provenance links (a genuine "no data" case).

resolve_evidence_source_views (BUG183 Defect 1) is SEPARATE from
resolve_citations: citations resolve only the E-refs the scoring LLM cited in
an axis, while evidence source views group EVERY bundle hit under every
source in its `sources` set, independent of what the scorer cited. A hit that
matched both PatentApi and SeedCorpus therefore appears on both cards.
"""

from collections.abc import Iterable
from dataclasses import dataclass

from harvesting.domain.models.axis_score import AxisScore
from harvesting.domain.models.citation import (
    Citation,
    HighlightRect,
    LineRange,
    PageDimension,
    ProvenanceLink,
)
from harvesting.domain.services.excerpt_formatter import build_clean_excerpt

# Backend source type -> frontend source type, mirroring scoring's _SOURCE_TYPE_MAP
# (get_verdict_handler.py). A hit may belong to several sources; the primary one
# is chosen by _SOURCE_PRIORITY order for a deterministic Citation.source_type.
_SOURCE_TYPE_MAP = {
    "PatentApi": "patent_api",
    "SeedCorpus": "vector_corpus",
    "LlmResearch": "llm_deep_research",
}
_SOURCE_PRIORITY = ["PatentApi", "SeedCorpus", "LlmResearch"]
_UNKNOWN_SOURCE_TYPE = "unknown"

_REF_PREFIX = "E"

# Mirrors resolve_source_availability's False default: a source with no known
# status (missing bundle, missing map, or missing key) is treated as "empty"
# rather than asserted "active".
_DEFAULT_SOURCE_STATUS = "empty"


def resolve_citations(
    axes: Iterable[AxisScore],
    evidence_bundle: dict | None,
) -> list[Citation]:
    """Resolve every unique axis ref to a Citation via the evidence bundle.

    Refs are deduped across all axes (first-seen order). A ref whose index does
    not exist in the bundle (stale/missing hit) is skipped, never raised.
    Returns an empty list when the bundle is None or has no hits.
    """
    hits = _bundle_hits(evidence_bundle)
    if not hits:
        return []
    sorted_hits = _sort_hits(hits)
    citations: list[Citation] = []
    for ref in _unique_refs(axes):
        hit = _hit_for_ref(ref, sorted_hits)
        if hit is not None:
            citations.append(_to_citation(ref, hit))
    return citations


def resolve_provenance_links(
    candidate: dict,
    chunk: dict | None = None,
    document: dict | None = None,
    provenance_entry: dict | None = None,
) -> list[ProvenanceLink]:
    """Build provenance links from the candidate's source_span + document_id.

    Mirrors scoring's ProvenanceDto shape. Returns an empty list only when the
    candidate carries neither a document id nor a source span.

    `chunk`, `document`, and `provenance_entry` are already-fetched Cosmos
    payloads (chunks / documents / provenance_maps containers respectively) --
    this function performs no I/O itself. Callers that cannot resolve one of
    these (legacy batch, chunk-read failure, ...) pass None and the resulting
    link degrades to preview_kind="none" with the pre-US123 fields intact.
    """
    source_span = candidate.get("source_span") or {}
    document_id = candidate.get("document_id", "")
    if not document_id and not source_span:
        return []
    return [
        _build_provenance_link(
            candidate, document_id, source_span, chunk, document, provenance_entry
        )
    ]


def _build_provenance_link(
    candidate: dict,
    document_id: str,
    source_span: dict,
    chunk: dict | None,
    document: dict | None,
    provenance_entry: dict | None,
) -> ProvenanceLink:
    source_chunk_index = candidate.get("source_chunk_index")
    chunk_id = _resolve_chunk_id(chunk, document_id, source_chunk_index)
    source_kind = source_span.get("source_kind", "")
    preview = _resolve_preview(source_kind, chunk, document, provenance_entry)
    return ProvenanceLink(
        document_id=document_id,
        locator=source_span.get("locator", ""),
        source_kind=source_kind,
        chunk_id=chunk_id,
        source_chunk_index=source_chunk_index,
        page_number=source_span.get("page_number"),
        section_hint=source_span.get("section_hint"),
        span_start=source_span.get("span_start"),
        span_end=source_span.get("span_end"),
        excerpt=source_span.get("excerpt"),
        preview_kind=preview.preview_kind,
        page_dimensions=preview.page_dimensions,
        highlight_rects=preview.highlight_rects,
        clean_excerpt=_resolve_clean_excerpt(chunk, source_span),
        file_path=preview.file_path,
        line_range=preview.line_range,
    )


def _resolve_chunk_id(
    chunk: dict | None, document_id: str, source_chunk_index: int | None
) -> str | None:
    if chunk is not None and chunk.get("id"):
        return str(chunk["id"])
    if document_id and source_chunk_index is not None:
        return f"{document_id}|{source_chunk_index}"
    return None


@dataclass(frozen=True)
class _PreviewResolution:
    preview_kind: str
    page_dimensions: list[PageDimension] | None = None
    highlight_rects: list[HighlightRect] | None = None
    file_path: str | None = None
    line_range: LineRange | None = None


_CODE_SOURCE_KIND = "code"


def _resolve_preview(
    source_kind: str,
    chunk: dict | None,
    document: dict | None,
    provenance_entry: dict | None,
) -> _PreviewResolution:
    if source_kind == _CODE_SOURCE_KIND:
        file_path, line_range = _resolve_code_metadata(provenance_entry)
        return _PreviewResolution(preview_kind="code", file_path=file_path, line_range=line_range)
    if _has_pdf_geometry(chunk, document):
        return _PreviewResolution(
            preview_kind="pdf",
            page_dimensions=[PageDimension(**d) for d in chunk["page_dimensions"]],
            highlight_rects=[HighlightRect(**r) for r in chunk["chunk_rects"]],
        )
    return _PreviewResolution(preview_kind="none")


def _has_pdf_geometry(chunk: dict | None, document: dict | None) -> bool:
    if not chunk or not document:
        return False
    if not document.get("viewable_blob_uri"):
        return False
    return bool(chunk.get("chunk_rects")) and bool(chunk.get("page_dimensions"))


def _resolve_code_metadata(provenance_entry: dict | None) -> tuple[str | None, LineRange | None]:
    if not provenance_entry:
        return None, None
    file_path = provenance_entry.get("file_path")
    raw_line_range = provenance_entry.get("line_range")
    if not raw_line_range or len(raw_line_range) != 2:
        return file_path, None
    return file_path, LineRange(start_line=raw_line_range[0], end_line=raw_line_range[1])


def _resolve_clean_excerpt(chunk: dict | None, source_span: dict) -> str | None:
    if not chunk or not chunk.get("text"):
        return None
    text = chunk["text"]
    chunk_start_char = chunk.get("start_char", 0)
    local_start, local_end = _resolve_local_span(text, chunk_start_char, source_span)
    return build_clean_excerpt(text, local_start, local_end)


def _resolve_local_span(text: str, chunk_start_char: int, source_span: dict) -> tuple[int, int]:
    span_start = source_span.get("span_start")
    span_end = source_span.get("span_end")
    if span_start is None or span_end is None:
        return 0, len(text)
    local_start = span_start - chunk_start_char
    local_end = span_end - chunk_start_char
    if local_start < 0 or local_end > len(text) or local_start >= local_end:
        return 0, len(text)
    return local_start, local_end


def _bundle_hits(evidence_bundle: dict | None) -> list[dict]:
    if not evidence_bundle:
        return []
    return evidence_bundle.get("hits") or []


def _sort_hits(hits: list[dict]) -> list[dict]:
    # Identical sort key to scoring (see module docstring). content_hash is
    # always present upstream; defaulted defensively for stale documents.
    return sorted(hits, key=lambda h: (h.get("patent_id") or "", h.get("content_hash", "")))


def _unique_refs(axes: Iterable[AxisScore]) -> list[str]:
    seen: dict[str, None] = {}
    for axis in axes:
        for ref in axis.refs:
            seen.setdefault(ref, None)
    return list(seen)


def _hit_for_ref(ref: str, sorted_hits: list[dict]) -> dict | None:
    index = _ref_to_index(ref)
    if index is None or not (0 <= index < len(sorted_hits)):
        return None
    return sorted_hits[index]


def _ref_to_index(ref: str) -> int | None:
    # Refs are "E1", "E2", ...  -> zero-based index (E{i + 1} in scoring).
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


def resolve_source_availability(evidence_bundle: dict | None) -> dict[str, bool]:
    """Map evidence bundle source_flags to frontend source availability keys.

    Returns a dict with all three frontend keys (patent_api, vector_corpus,
    llm_deep_research) present, defaulting to False when the bundle is None,
    source_flags is missing, or a flag is absent. Handles source_flags keys
    as either enum instances or string values robustly.
    """
    if not evidence_bundle:
        return {
            "patent_api": False,
            "vector_corpus": False,
            "llm_deep_research": False,
        }
    source_flags = evidence_bundle.get("source_flags") or {}
    availability = {}
    for backend_key, frontend_key in _SOURCE_TYPE_MAP.items():
        flag_value = source_flags.get(backend_key, False)
        availability[frontend_key] = bool(flag_value)
    for frontend_key in _SOURCE_TYPE_MAP.values():
        availability.setdefault(frontend_key, False)
    return availability


def resolve_source_status(evidence_bundle: dict | None) -> dict[str, str]:
    """Map evidence bundle source_status to frontend source status keys.

    Returns a dict with all three frontend keys (patent_api, vector_corpus,
    llm_deep_research) present, defaulting to "empty" when the bundle is
    None, source_status is missing (older events predating this field), or a
    given source's status is absent. Mirrors resolve_source_availability's
    key set and False-default behavior, but with the richer SourceStatus
    vocabulary (active, empty, filtered, error, timeout).
    """
    if not evidence_bundle:
        return {frontend_key: _DEFAULT_SOURCE_STATUS for frontend_key in _SOURCE_TYPE_MAP.values()}
    source_status = evidence_bundle.get("source_status") or {}
    status = {}
    for backend_key, frontend_key in _SOURCE_TYPE_MAP.items():
        status_value = source_status.get(backend_key)
        status[frontend_key] = str(status_value) if status_value else _DEFAULT_SOURCE_STATUS
    for frontend_key in _SOURCE_TYPE_MAP.values():
        status.setdefault(frontend_key, _DEFAULT_SOURCE_STATUS)
    return status


def _primary_source_type(hit: dict) -> str:
    sources = hit.get("sources") or []
    for source in _SOURCE_PRIORITY:
        if source in sources:
            return _SOURCE_TYPE_MAP[source]
    return _UNKNOWN_SOURCE_TYPE


_PATENT_API_KEY = _SOURCE_TYPE_MAP["PatentApi"]
_LLM_KEY = _SOURCE_TYPE_MAP["LlmResearch"]


@dataclass(frozen=True)
class _SourceViewContext:
    availability: dict[str, bool]
    status: dict[str, str]
    hits_by_source: dict[str, list[dict]]
    llm_research: dict | None
    degraded: bool
    degraded_sources: list[str]


def resolve_evidence_source_views(evidence_bundle: dict | None) -> list[dict]:
    """Build one EvidenceSourceView per frontend source, independent of citations.

    Every bundle hit is grouped under EVERY source in its `sources` set (not
    just a "primary" one), so a hit that matched both PatentApi and SeedCorpus
    appears on both cards. Confidence is the mean similarity of a source's own
    hits, except for the LLM view which uses `llm_research.confidence` when
    present. Reasoning (LLM findings) and degraded/degraded_sources (patent
    coverage) are attached only to their respective cards. Returns one view
    per known frontend source key even when the bundle is None or empty.
    """
    degraded, degraded_sources = _patent_degradation(evidence_bundle)
    ctx = _SourceViewContext(
        availability=resolve_source_availability(evidence_bundle),
        status=resolve_source_status(evidence_bundle),
        hits_by_source=_group_hits_by_source(evidence_bundle),
        llm_research=_llm_research_summary(evidence_bundle),
        degraded=degraded,
        degraded_sources=degraded_sources,
    )
    return [_build_source_view(source_type, ctx) for source_type in _SOURCE_TYPE_MAP.values()]


def _build_source_view(source_type: str, ctx: _SourceViewContext) -> dict:
    hits = ctx.hits_by_source.get(source_type, [])
    is_patent_api = source_type == _PATENT_API_KEY
    return {
        "source_type": source_type,
        "available": ctx.availability[source_type],
        "status": ctx.status[source_type],
        "hits": hits,
        "confidence": _resolve_confidence(source_type, hits, ctx.llm_research),
        "reasoning": _resolve_reasoning(source_type, ctx.llm_research),
        "degraded": ctx.degraded if is_patent_api else False,
        "degraded_sources": ctx.degraded_sources if is_patent_api else [],
    }


def _group_hits_by_source(evidence_bundle: dict | None) -> dict[str, list[dict]]:
    grouped: dict[str, list[dict]] = {key: [] for key in _SOURCE_TYPE_MAP.values()}
    for hit in _sort_hits(_bundle_hits(evidence_bundle)):
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


def _llm_research_summary(evidence_bundle: dict | None) -> dict | None:
    if not evidence_bundle:
        return None
    return evidence_bundle.get("llm_research")


def _patent_degradation(evidence_bundle: dict | None) -> tuple[bool, list[str]]:
    if not evidence_bundle:
        return False, []
    return (
        bool(evidence_bundle.get("degraded", False)),
        list(evidence_bundle.get("degraded_sources") or []),
    )


def _mean_similarity(hits: list[dict]) -> float:
    if not hits:
        return 0.0
    return sum(float(hit.get("similarity", 0.0)) for hit in hits) / len(hits)


def _resolve_confidence(source_type: str, hits: list[dict], llm_research: dict | None) -> float:
    if source_type == _LLM_KEY and llm_research is not None:
        return float(llm_research.get("confidence", 0.0))
    return _mean_similarity(hits)


def _resolve_reasoning(source_type: str, llm_research: dict | None) -> list[str]:
    if source_type != _LLM_KEY or llm_research is None:
        return []
    return list(llm_research.get("findings") or [])
