import asyncio
import logging
from dataclasses import dataclass

from harvesting.application.dtos.assemble_request import AssembleCandidateDto
from harvesting.domain.enums.agreement_flag import AgreementFlag
from harvesting.domain.enums.maturity import Maturity
from harvesting.domain.enums.scoring_axis import ScoringAxis
from harvesting.domain.models.axis_score import AxisScore
from harvesting.domain.models.rank_weights import RankWeights
from harvesting.domain.models.ranked_candidate import RankedCandidate
from harvesting.domain.models.scored_candidate import ScoredCandidate
from harvesting.domain.services import harvesting_ranker, maturity_classifier
from harvesting.domain.services.evidence_citation_resolver import (
    resolve_citations,
    resolve_evidence_source_views,
    resolve_provenance_links,
    resolve_source_availability,
    resolve_source_status,
)
from harvesting.infrastructure.config.settings import HarvestingSettings

logger = logging.getLogger(__name__)

_TITLE_MAX_CHARS = 80


@dataclass
class ChunkGeometryRepos:
    """Optional read repos used to enrich provenance links with US122 geometry.

    Any/all fields may be None (legacy batches, tests, or repos not wired in
    this deployment) -- every lookup degrades to preview_kind="none" rather
    than failing candidate assembly.
    """

    chunk_repo: object | None = None
    document_repo: object | None = None
    provenance_repo: object | None = None


async def _fetch_chunk_geometry(
    candidate: dict, repos: ChunkGeometryRepos | None
) -> tuple[dict | None, dict | None, dict | None]:
    if repos is None or repos.chunk_repo is None:
        return None, None, None
    batch_id = candidate.get("batch_id", "")
    document_id = candidate.get("document_id", "")
    order_index = candidate.get("source_chunk_index")
    if not batch_id or not document_id or order_index is None:
        return None, None, None
    chunk = await _fetch_chunk(repos.chunk_repo, batch_id, document_id, order_index)
    if chunk is None:
        return None, None, None
    document = await _fetch_document(repos.document_repo, batch_id, document_id)
    provenance_entry = await _fetch_provenance_entry(repos.provenance_repo, batch_id, chunk)
    return chunk, document, provenance_entry


async def _fetch_chunk(
    chunk_repo: object, batch_id: str, document_id: str, order_index: int
) -> dict | None:
    try:
        return await chunk_repo.get_by_document_order(batch_id, document_id, order_index)
    except Exception as exc:
        logger.warning(
            "batch_id=%s document_id=%s order_index=%s reason=chunk_read_failed error=%s",
            batch_id,
            document_id,
            order_index,
            exc,
        )
        return None


async def _fetch_document(
    document_repo: object | None, batch_id: str, document_id: str
) -> dict | None:
    if document_repo is None:
        return None
    try:
        return await document_repo.get(batch_id, document_id)
    except Exception as exc:
        logger.warning(
            "batch_id=%s document_id=%s reason=document_read_failed error=%s",
            batch_id,
            document_id,
            exc,
        )
        return None


async def _fetch_provenance_entry(
    provenance_repo: object | None, batch_id: str, chunk: dict
) -> dict | None:
    chunk_id = chunk.get("id")
    if provenance_repo is None or not chunk_id:
        return None
    try:
        return await provenance_repo.get(batch_id, chunk_id)
    except Exception as exc:
        logger.warning(
            "batch_id=%s chunk_id=%s reason=provenance_entry_read_failed error=%s",
            batch_id,
            chunk_id,
            exc,
        )
        return None


def _cap_text(text: str, max_chars: int = _TITLE_MAX_CHARS) -> str:
    return text[:max_chars].rstrip() + "…" if len(text) > max_chars else text


def _first_sentence(text: str) -> str:
    return text.split(". ")[0].strip()


def _derive_title(candidate: dict) -> str:
    existing = candidate.get("title")
    if existing:
        return existing
    claim_text = candidate.get("claim_text", "").strip()
    if claim_text:
        return _cap_text(_first_sentence(claim_text))
    for key in ("problem", "tech_field"):
        value = candidate.get(key, "").strip()
        if value:
            return _cap_text(value)
    return "Untitled invention candidate"


def _derive_description(candidate: dict) -> str:
    existing = candidate.get("description")
    if existing:
        return existing
    problem = candidate.get("problem", "").strip()
    mechanism = candidate.get("mechanism", "").strip()
    parts = [p for p in (problem, mechanism) if p]
    if parts:
        return " ".join(parts)
    return candidate.get("claim_text", "").strip()


def _candidate_key(item: dict) -> str | None:
    return item.get("id") or item.get("candidate_id")


def _index_candidates(items: list[dict]) -> dict[str, dict]:
    return {key: item for item in items if (key := _candidate_key(item)) is not None}


def _index_by_candidate_id(items: list[dict]) -> dict[str, dict]:
    return {item["candidate_id"]: item for item in items if "candidate_id" in item}


def _parse_axis_score(axis: ScoringAxis, data: dict) -> AxisScore:
    return AxisScore(axis=axis, score=data.get("score", 0), refs=data.get("refs", []))


def _build_axes(verdict: dict) -> dict[ScoringAxis, AxisScore]:
    raw_axes: dict = verdict.get("axes", {})
    return {
        axis: _parse_axis_score(axis, raw_axes[axis.value])
        for axis in ScoringAxis
        if axis.value in raw_axes
    }


def _build_scored_candidate(
    candidate_id: str, batch_id: str, document_id: str, axes: dict[ScoringAxis, AxisScore]
) -> ScoredCandidate:
    return ScoredCandidate(
        candidate_id=candidate_id,
        batch_id=batch_id,
        job_id=batch_id,
        document_id=document_id,
        axes=axes,
    )


def _resolve_maturity(scored: ScoredCandidate, settings: HarvestingSettings) -> Maturity:
    result = maturity_classifier.classify(
        scored,
        novelty_threshold=settings.maturity_novelty_threshold,
        feasibility_threshold=settings.maturity_feasibility_threshold,
    )
    return result.maturity


def _resolve_agreement_flag(candidate: dict) -> AgreementFlag:
    raw = candidate.get("agreement_flag", AgreementFlag.Full.value)
    try:
        return AgreementFlag(raw)
    except ValueError:
        return AgreementFlag.Full


async def _to_assemble_dto(
    candidate: dict,
    ranked: RankedCandidate,
    maturity: Maturity,
    axes: dict[ScoringAxis, AxisScore],
    evidence_bundle: dict | None,
    verdict: dict | None,
    geometry_repos: ChunkGeometryRepos | None,
) -> AssembleCandidateDto:
    claim_draft = ""
    if verdict:
        claim_draft = verdict.get("drafted_claim", "").strip()
    chunk, document, provenance_entry = await _fetch_chunk_geometry(candidate, geometry_repos)
    return AssembleCandidateDto(
        candidate_id=ranked.candidate_id,
        title=_derive_title(candidate),
        description=_derive_description(candidate),
        claim_text=candidate.get("claim_text", "").strip(),
        claim_draft=claim_draft,
        maturity=maturity,
        rank=ranked.rank,
        weighted_score=ranked.weighted_score,
        axes=axes,
        agreement_flag=_resolve_agreement_flag(candidate),
        citations=resolve_citations(axes.values(), evidence_bundle),
        provenance_links=resolve_provenance_links(candidate, chunk, document, provenance_entry),
        source_availability=resolve_source_availability(evidence_bundle),
        source_status=resolve_source_status(evidence_bundle),
        evidence_sources=resolve_evidence_source_views(evidence_bundle),
    )


def _score_and_index(
    candidate_map: dict[str, dict],
    verdict_map: dict[str, dict],
) -> tuple[list[ScoredCandidate], dict[str, dict[ScoringAxis, AxisScore]]]:
    scored_list: list[ScoredCandidate] = []
    axes_per: dict[str, dict[ScoringAxis, AxisScore]] = {}
    for candidate_id, verdict in verdict_map.items():
        if candidate_id not in candidate_map:
            continue
        cand = candidate_map[candidate_id]
        axes = _build_axes(verdict)
        scored = _build_scored_candidate(
            candidate_id=candidate_id,
            batch_id=cand.get("batch_id", ""),
            document_id=cand.get("document_id", ""),
            axes=axes,
        )
        scored_list.append(scored)
        axes_per[candidate_id] = axes
    return scored_list, axes_per


async def assemble_candidates(
    candidates: list[dict],
    verdicts: list[dict],
    weights: RankWeights,
    settings: HarvestingSettings,
    evidence_bundles: dict[str, dict] | None = None,
    geometry_repos: ChunkGeometryRepos | None = None,
) -> list[AssembleCandidateDto]:
    candidate_map = _index_candidates(candidates)
    verdict_map = _index_by_candidate_id(verdicts)
    evidence_map = evidence_bundles or {}

    scored_list, axes_per = _score_and_index(candidate_map, verdict_map)
    if not scored_list:
        return []

    ranked_list = harvesting_ranker.rank(scored_list, weights)
    ranked_map: dict[str, RankedCandidate] = {r.candidate_id: r for r in ranked_list}

    result = await asyncio.gather(
        *(
            _to_assemble_dto(
                candidate_map[sc.candidate_id],
                ranked_map[sc.candidate_id],
                _resolve_maturity(sc, settings),
                axes_per[sc.candidate_id],
                evidence_map.get(sc.candidate_id),
                verdict_map.get(sc.candidate_id),
                geometry_repos,
            )
            for sc in scored_list
        )
    )
    result.sort(key=lambda d: d.rank)
    return result
