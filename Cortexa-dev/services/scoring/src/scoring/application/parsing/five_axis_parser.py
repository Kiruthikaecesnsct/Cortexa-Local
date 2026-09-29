import logging

from scoring.application.parsing.json_extractor import extract_json
from scoring.domain.enums.scoring_axis import ScoringAxis
from scoring.domain.errors.scoring_errors import AxisParseError, UngroundedVerdictError
from scoring.domain.models.axis_score import AxisScore
from scoring.domain.models.evidence_bundle import EvidenceBundle
from scoring.domain.models.scoring_verdict import ScoringVerdict

logger = logging.getLogger(__name__)

_ALL_AXES = list(ScoringAxis)


def _build_valid_refs(bundle: EvidenceBundle) -> set[str]:
    # Mirrored in harvesting's evidence_citation_resolver.py — keep both sort keys identical.
    sorted_hits = sorted(bundle.hits, key=lambda h: (h.patent_id or "", h.content_hash))
    return {f"E{i + 1}" for i in range(len(sorted_hits))}


def _extract_score(entry: dict, key: str) -> int:
    raw_score = entry.get("score")
    if raw_score is None:
        raise AxisParseError(f"axis '{key}' missing 'score' field")
    if not isinstance(raw_score, (int, float)):
        raise AxisParseError(f"axis '{key}' score must be a number, got {type(raw_score).__name__}")
    return int(raw_score)


def _extract_refs(entry: dict, key: str) -> list[str]:
    raw_refs = entry.get("refs")
    if raw_refs is None:
        raise AxisParseError(f"axis '{key}' missing 'refs' field")
    if not isinstance(raw_refs, list):
        raise AxisParseError(f"axis '{key}' refs must be a list, got {type(raw_refs).__name__}")
    return raw_refs


def _validate_refs(refs: list[str], valid_refs: set[str], key: str) -> None:
    if not refs:
        logger.warning(
            "axis '%s' rejected: refs list is empty (expected citation from evidence bundle)", key
        )
        raise UngroundedVerdictError(f"axis '{key}' has no evidence citations")
    hallucinated = [r for r in refs if r not in valid_refs]
    if hallucinated:
        logger.warning(
            "axis '%s' rejected: hallucinated refs %s not in evidence bundle (valid: %s)",
            key,
            hallucinated,
            sorted(valid_refs),
        )
        raise UngroundedVerdictError(
            f"axis '{key}' cites refs {hallucinated} not present in evidence bundle"
        )


def _parse_axis(axis: ScoringAxis, raw_axes: dict, valid_refs: set[str]) -> AxisScore:
    key = axis.value
    if key not in raw_axes:
        raise AxisParseError(f"missing axis '{key}' in model response")
    entry = raw_axes[key]
    if not isinstance(entry, dict):
        raise AxisParseError(f"axis '{key}' must be a JSON object, got {type(entry).__name__}")
    score = _extract_score(entry, key)
    refs = _extract_refs(entry, key)
    _validate_refs(refs, valid_refs, key)
    try:
        return AxisScore(axis=axis, score=score, refs=refs)
    except Exception as exc:
        raise AxisParseError(f"axis '{key}' failed validation: {exc}") from exc


def parse_five_axes(raw_response: str, bundle: EvidenceBundle) -> ScoringVerdict:
    raw_axes = extract_json(raw_response)
    valid_refs = _build_valid_refs(bundle)
    axes: dict[ScoringAxis, AxisScore] = {}
    for axis in _ALL_AXES:
        axes[axis] = _parse_axis(axis, raw_axes, valid_refs)
    return ScoringVerdict(
        batch_id=bundle.batch_id,
        job_id=bundle.job_id,
        candidate_id=bundle.candidate_id,
        document_id=bundle.document_id,
        axes=axes,
    )
