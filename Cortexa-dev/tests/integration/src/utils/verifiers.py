import pytest


def resolve_candidate_id(item: dict, label: str) -> str:
    """Return the item's candidate id, trying candidate_id then id. Skips the test
    when neither is present, so callers need no duplicated skip block."""
    candidate_id = item.get("candidate_id") or item.get("id")
    if not candidate_id:
        pytest.skip(f"first {label} has no resolvable candidate_id field; keys: {list(item)}")
    return candidate_id


def assert_both_engines_present(results: dict) -> None:
    assert results.get("harvesting") is not None, "harvesting must be present for dual-engine run"
    assert results.get("seeding") is not None, "seeding must be present for dual-engine run"


def assert_harvesting_shape(harvesting: dict) -> None:
    assert harvesting.get("batch_id"), "harvesting.batch_id must be set"
    assert isinstance(harvesting.get("candidates"), list), "harvesting.candidates must be a list"
    assert isinstance(harvesting.get("verdicts"), list), "harvesting.verdicts must be a list"


def assert_seeding_shape(seeding: dict) -> None:
    assert seeding.get("batch_id"), "seeding.batch_id must be set"
    assert isinstance(seeding.get("opportunities"), list), "seeding.opportunities must be a list"


def assert_verdict_has_citations(detail: dict) -> None:
    axis_scores = detail.get("axis_scores", [])
    assert len(axis_scores) > 0, "candidate detail must have at least one axis_score"
    for score in axis_scores:
        assert isinstance(score.get("citations"), list), (
            f"axis score '{score.get('axis')}' is missing a citations list"
        )


def assert_evidence_sources_non_empty(detail: dict) -> None:
    sources = detail.get("evidence_sources", [])
    assert len(sources) > 0, "evidence_sources must not be empty"
    for source in sources:
        if not source.get("available"):
            continue
        citations = source.get("citations", [])
        assert len(citations) > 0, (
            f"available source '{source.get('source_type')}' has no citations"
        )
