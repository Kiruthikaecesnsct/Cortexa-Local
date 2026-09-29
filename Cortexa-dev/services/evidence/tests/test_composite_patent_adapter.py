import logging
from unittest.mock import AsyncMock

import httpx
import pytest

from evidence.domain.enums.patent_source_name import PatentSourceName
from evidence.domain.enums.patent_source_outcome import PatentSourceOutcome
from evidence.domain.errors.evidence_errors import PatentApiError, PatentAuthError
from evidence.domain.models.patent_match import PatentMatch
from evidence.infrastructure.patent_apis.composite_patent_adapter import CompositePatentAdapter

QUERY = "sparse tensor quantization"
LIMIT = 10

USPTO_SECRET = "secret-uspto-key-abc123"
EPO_SECRET = "secret-epo-key-xyz789"
LENS_SECRET = "secret-lens-key-def456"


def _make_match(
    reference: str,
    score: float = 0.8,
    abstract: str = "",
    claims: list[str] | None = None,
    jurisdiction: str = "",
) -> PatentMatch:
    return PatentMatch(
        reference=reference,
        title=f"Title for {reference}",
        applicant="Test Corp",
        date="2024-01-01",
        url=f"https://patents/{reference}",
        relevance_score=score,
        abstract=abstract,
        claims=claims or [],
        jurisdiction=jurisdiction,
    )


def _make_adapter(
    uspto_result=None,
    epo_result=None,
    lens_result=None,
) -> CompositePatentAdapter:
    uspto = AsyncMock()
    epo = AsyncMock()
    lens = AsyncMock()

    if isinstance(uspto_result, BaseException):
        uspto.search.side_effect = uspto_result
    else:
        uspto.search.return_value = uspto_result or []

    if isinstance(epo_result, BaseException):
        epo.search.side_effect = epo_result
    else:
        epo.search.return_value = epo_result or []

    if isinstance(lens_result, BaseException):
        lens.search.side_effect = lens_result
    else:
        lens.search.return_value = lens_result or []

    return CompositePatentAdapter(uspto=uspto, epo=epo, lens=lens)


@pytest.mark.asyncio
async def test_all_sources_ok():
    uspto_matches = [_make_match("US11111111", 0.9)]
    epo_matches = [_make_match("EP3456789", 0.8)]
    lens_matches = [_make_match("US22222222", 0.7)]

    adapter = _make_adapter(
        uspto_result=uspto_matches,
        epo_result=epo_matches,
        lens_result=lens_matches,
    )

    matches, source_results = await adapter.search(QUERY, LIMIT)

    assert len(source_results) == 3
    assert all(r.outcome == PatentSourceOutcome.ok for r in source_results)

    by_source = {r.source: r for r in source_results}
    assert by_source[PatentSourceName.USPTO].hit_count == 1
    assert by_source[PatentSourceName.EPO].hit_count == 1
    assert by_source[PatentSourceName.Lens].hit_count == 1

    assert len(matches) == 3


@pytest.mark.asyncio
async def test_all_sources_ok_no_secrets_in_log_extras(caplog):
    uspto_matches = [_make_match("US11111111")]
    adapter = _make_adapter(
        uspto_result=uspto_matches,
        epo_result=[],
        lens_result=[],
    )

    with caplog.at_level(logging.INFO):
        await adapter.search(QUERY, LIMIT)

    for record in caplog.records:
        extra = getattr(record, "__dict__", {})
        for secret in (USPTO_SECRET, EPO_SECRET, LENS_SECRET):
            assert secret not in str(extra)
        if hasattr(record, "error_detail") and record.error_detail:
            assert USPTO_SECRET not in record.error_detail
            assert EPO_SECRET not in record.error_detail
            assert LENS_SECRET not in record.error_detail


@pytest.mark.asyncio
async def test_one_source_auth_error():
    epo_matches = [_make_match("EP3456789", 0.8)]
    lens_matches = [_make_match("US22222222", 0.7)]

    adapter = _make_adapter(
        uspto_result=PatentAuthError("Invalid credentials", status_code=401),
        epo_result=epo_matches,
        lens_result=lens_matches,
    )

    matches, source_results = await adapter.search(QUERY, LIMIT)

    by_source = {r.source: r for r in source_results}
    assert by_source[PatentSourceName.USPTO].outcome == PatentSourceOutcome.auth_error
    assert by_source[PatentSourceName.EPO].outcome == PatentSourceOutcome.ok
    assert by_source[PatentSourceName.Lens].outcome == PatentSourceOutcome.ok

    refs = {m.reference for m in matches}
    assert "US11111111" not in refs
    assert "EP3456789" in refs
    assert "US22222222" in refs


async def test_stale_credential_403_classified_as_auth_error_not_schema_error():
    epo_matches = [_make_match("EP3456789", 0.8)]
    lens_matches = [_make_match("US22222222", 0.7)]

    adapter = _make_adapter(
        uspto_result=PatentAuthError("USPTO returned 403", status_code=403),
        epo_result=epo_matches,
        lens_result=lens_matches,
    )

    matches, source_results = await adapter.search(QUERY, LIMIT)

    by_source = {r.source: r for r in source_results}
    assert by_source[PatentSourceName.USPTO].outcome == PatentSourceOutcome.auth_error
    assert by_source[PatentSourceName.USPTO].outcome != PatentSourceOutcome.schema_error
    assert by_source[PatentSourceName.EPO].outcome == PatentSourceOutcome.ok
    assert by_source[PatentSourceName.Lens].outcome == PatentSourceOutcome.ok

    refs = {m.reference for m in matches}
    assert "EP3456789" in refs
    assert "US22222222" in refs


@pytest.mark.asyncio
async def test_rate_limited_classified_correctly():
    request = httpx.Request("POST", "https://api.lens.org/patent/search")
    response = httpx.Response(429, request=request)
    lens_error = httpx.HTTPStatusError("429 Too Many Requests", request=request, response=response)

    adapter = _make_adapter(
        uspto_result=[_make_match("US11111111")],
        epo_result=[_make_match("EP3456789")],
        lens_result=lens_error,
    )

    matches, source_results = await adapter.search(QUERY, LIMIT)

    by_source = {r.source: r for r in source_results}
    assert by_source[PatentSourceName.Lens].outcome == PatentSourceOutcome.rate_limited
    assert by_source[PatentSourceName.USPTO].outcome == PatentSourceOutcome.ok
    assert by_source[PatentSourceName.EPO].outcome == PatentSourceOutcome.ok


@pytest.mark.asyncio
async def test_all_sources_failed():
    adapter = _make_adapter(
        uspto_result=PatentApiError("USPTO down", status_code=503),
        epo_result=PatentApiError("EPO down", status_code=503),
        lens_result=PatentApiError("Lens down", status_code=503),
    )

    matches, source_results = await adapter.search(QUERY, LIMIT)

    assert matches == []
    assert len(source_results) == 3
    assert all(r.outcome != PatentSourceOutcome.ok for r in source_results)
    assert all(r.hit_count == 0 for r in source_results)


@pytest.mark.asyncio
async def test_no_api_key_in_logs(caplog):
    fake_key = "SUPER_SECRET_KEY_DO_NOT_LOG"
    error = PatentApiError(f"error with key {fake_key}", status_code=400)

    adapter = _make_adapter(
        uspto_result=error,
        epo_result=[],
        lens_result=[],
    )

    with caplog.at_level(logging.INFO):
        await adapter.search(QUERY, LIMIT)

    for record in caplog.records:
        record_dict = record.__dict__
        for value in record_dict.values():
            assert fake_key not in str(value)


@pytest.mark.asyncio
async def test_duplicate_references_keeps_highest_score():
    ref = "US11111111"
    uspto_matches = [_make_match(ref, score=0.6)]
    epo_matches = [_make_match(ref, score=0.9)]

    adapter = _make_adapter(
        uspto_result=uspto_matches,
        epo_result=epo_matches,
        lens_result=[],
    )

    matches, _ = await adapter.search(QUERY, LIMIT)

    assert len(matches) == 1
    assert matches[0].relevance_score == 0.9


@pytest.mark.asyncio
async def test_empty_reference_skipped():
    invalid = PatentMatch(
        reference="",
        title="No Reference",
        applicant="Corp",
        date="2024-01-01",
        url="",
        relevance_score=0.5,
    )
    adapter = _make_adapter(
        uspto_result=[invalid],
        epo_result=[],
        lens_result=[],
    )

    matches, source_results = await adapter.search(QUERY, LIMIT)

    assert matches == []
    assert source_results[0].hit_count == 1


@pytest.mark.asyncio
async def test_latency_recorded():
    adapter = _make_adapter(
        uspto_result=[_make_match("US11111111")],
        epo_result=[],
        lens_result=[],
    )

    _, source_results = await adapter.search(QUERY, LIMIT)

    for r in source_results:
        assert r.latency_ms >= 0.0


@pytest.mark.asyncio
async def test_rate_limited_internally_then_recovered_outcome_is_ok():
    adapter = _make_adapter(
        uspto_result=[_make_match("US11111111")],
        epo_result=[_make_match("EP3456789")],
        lens_result=[_make_match("US22222222")],
    )

    _, source_results = await adapter.search(QUERY, LIMIT)

    assert all(r.outcome == PatentSourceOutcome.ok for r in source_results)
    assert all(r.hit_count > 0 for r in source_results)


@pytest.mark.asyncio
async def test_duplicate_backfills_empty_enrichment_from_loser():
    ref = "US11111111"
    winner = _make_match(ref, score=0.9)
    loser = _make_match(
        ref,
        score=0.6,
        abstract="A detailed abstract",
        claims=["claim one", "claim two"],
        jurisdiction="US",
    )

    adapter = _make_adapter(
        uspto_result=[winner],
        epo_result=[loser],
        lens_result=[],
    )

    matches, _ = await adapter.search(QUERY, LIMIT)

    assert len(matches) == 1
    assert matches[0].relevance_score == 0.9
    assert matches[0].abstract == "A detailed abstract"
    assert matches[0].claims == ["claim one", "claim two"]
    assert matches[0].jurisdiction == "US"


@pytest.mark.asyncio
async def test_duplicate_does_not_overwrite_existing_enrichment():
    ref = "US11111111"
    winner = _make_match(
        ref,
        score=0.9,
        abstract="Winner abstract",
        claims=["winner claim"],
        jurisdiction="EP",
    )
    loser = _make_match(
        ref,
        score=0.6,
        abstract="Loser abstract",
        claims=["loser claim"],
        jurisdiction="US",
    )

    adapter = _make_adapter(
        uspto_result=[winner],
        epo_result=[loser],
        lens_result=[],
    )

    matches, _ = await adapter.search(QUERY, LIMIT)

    assert len(matches) == 1
    assert matches[0].abstract == "Winner abstract"
    assert matches[0].claims == ["winner claim"]
    assert matches[0].jurisdiction == "EP"


@pytest.mark.asyncio
async def test_disabled_source_is_not_called():
    adapter = _make_adapter(
        uspto_result=[_make_match("US11111111")],
        epo_result=[_make_match("EP3456789")],
        lens_result=[_make_match("US22222222")],
    )

    enabled = frozenset({PatentSourceName.USPTO, PatentSourceName.EPO})
    matches, source_results = await adapter.search(QUERY, LIMIT, enabled=enabled)

    adapter._lens.search.assert_not_called()
    adapter._uspto.search.assert_awaited_once()
    adapter._epo.search.assert_awaited_once()

    sources_seen = {r.source for r in source_results}
    assert sources_seen == {PatentSourceName.USPTO, PatentSourceName.EPO}
    refs = {m.reference for m in matches}
    assert "US22222222" not in refs


@pytest.mark.asyncio
async def test_two_disabled_sources_only_one_source_called():
    adapter = _make_adapter(
        uspto_result=[_make_match("US11111111")],
        epo_result=[_make_match("EP3456789")],
        lens_result=[_make_match("US22222222")],
    )

    enabled = frozenset({PatentSourceName.USPTO})
    matches, source_results = await adapter.search(QUERY, LIMIT, enabled=enabled)

    adapter._epo.search.assert_not_called()
    adapter._lens.search.assert_not_called()
    adapter._uspto.search.assert_awaited_once()

    assert len(source_results) == 1
    assert source_results[0].source == PatentSourceName.USPTO
    refs = {m.reference for m in matches}
    assert refs == {"US11111111"}


@pytest.mark.asyncio
async def test_all_sources_disabled_returns_no_matches_and_no_source_results():
    adapter = _make_adapter(
        uspto_result=[_make_match("US11111111")],
        epo_result=[_make_match("EP3456789")],
        lens_result=[_make_match("US22222222")],
    )

    matches, source_results = await adapter.search(QUERY, LIMIT, enabled=frozenset())

    adapter._uspto.search.assert_not_called()
    adapter._epo.search.assert_not_called()
    adapter._lens.search.assert_not_called()
    assert matches == []
    assert source_results == []


@pytest.mark.asyncio
async def test_enabled_none_defaults_to_all_sources_called():
    adapter = _make_adapter(
        uspto_result=[_make_match("US11111111")],
        epo_result=[_make_match("EP3456789")],
        lens_result=[_make_match("US22222222")],
    )

    matches, source_results = await adapter.search(QUERY, LIMIT)

    adapter._uspto.search.assert_awaited_once()
    adapter._epo.search.assert_awaited_once()
    adapter._lens.search.assert_awaited_once()
    assert len(source_results) == 3


@pytest.mark.asyncio
async def test_disabled_source_with_missing_key_does_not_affect_enabled_source_failure():
    adapter = _make_adapter(
        uspto_result=[_make_match("US11111111")],
        epo_result=PatentAuthError("EPO key missing", status_code=401),
        lens_result=[_make_match("US22222222")],
    )

    enabled = frozenset({PatentSourceName.USPTO, PatentSourceName.EPO})
    matches, source_results = await adapter.search(QUERY, LIMIT, enabled=enabled)

    adapter._lens.search.assert_not_called()
    by_source = {r.source: r for r in source_results}
    assert by_source[PatentSourceName.EPO].outcome == PatentSourceOutcome.auth_error
    assert by_source[PatentSourceName.USPTO].outcome == PatentSourceOutcome.ok


@pytest.mark.asyncio
async def test_rate_limited_exhausted_classified_as_rate_limited():
    request = httpx.Request("POST", "http://test")
    response = httpx.Response(429, request=request)
    error = httpx.HTTPStatusError("429 Too Many Requests", request=request, response=response)

    adapter = _make_adapter(
        uspto_result=error,
        epo_result=[_make_match("EP3456789")],
        lens_result=[_make_match("US22222222")],
    )

    _, source_results = await adapter.search(QUERY, LIMIT)

    by_source = {r.source: r for r in source_results}
    assert by_source[PatentSourceName.USPTO].outcome == PatentSourceOutcome.rate_limited
    assert by_source[PatentSourceName.EPO].outcome == PatentSourceOutcome.ok
    assert by_source[PatentSourceName.Lens].outcome == PatentSourceOutcome.ok
