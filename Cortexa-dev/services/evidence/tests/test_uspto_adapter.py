import asyncio
import os
from unittest.mock import AsyncMock

import httpx
import pytest
import respx

from evidence.domain.enums.evidence_source import EvidenceSource
from evidence.domain.errors.evidence_errors import PatentApiError, PatentAuthError
from evidence.infrastructure.config.settings import EvidenceSettings
from evidence.infrastructure.patent_apis.patent_adapter import RateLimiter
from evidence.infrastructure.patent_apis.uspto_adapter import (
    _USPTO_RETRY_AFTER_FLOOR_SECONDS,
    UsptoAdapter,
    _parse_claims_bag,
)
from tests.conftest import USPTO_BASE


@pytest.fixture(autouse=True)
def _fast_sleep(monkeypatch):
    monkeypatch.setattr(asyncio, "sleep", AsyncMock())
    yield


async def test_success_returns_patent_matches(evidence_settings, fake_secrets, mock_uspto):
    async with httpx.AsyncClient() as client:
        adapter = UsptoAdapter(evidence_settings, fake_secrets, client)
        results = await adapter.search("sparse tensor quantization", limit=5)

    assert len(results) == 2
    assert results[0].reference == "US11234567"
    assert results[0].title == "Method for sparse tensor quantization"
    assert results[0].applicant == "Acme Corp"
    assert results[0].date == "2024-03-15"
    assert "11234567" in results[0].url
    assert results[0].source is EvidenceSource.PatentApi


async def test_relevance_scores_in_bounds(evidence_settings, fake_secrets, mock_uspto):
    async with httpx.AsyncClient() as client:
        adapter = UsptoAdapter(evidence_settings, fake_secrets, client)
        results = await adapter.search("q", limit=5)
    for r in results:
        assert 0.0 <= r.relevance_score <= 1.0


async def test_empty_results_returns_empty_list(evidence_settings, fake_secrets, mock_uspto_empty):
    async with httpx.AsyncClient() as client:
        adapter = UsptoAdapter(evidence_settings, fake_secrets, client)
        results = await adapter.search("nonexistent topic", limit=5)
    assert results == []


async def test_400_raises_patent_api_error_immediately(
    evidence_settings, fake_secrets, mock_uspto_400
):
    async with httpx.AsyncClient() as client:
        adapter = UsptoAdapter(evidence_settings, fake_secrets, client)
        with pytest.raises(PatentApiError) as exc_info:
            await adapter.search("bad query", limit=5)
    assert exc_info.value.status_code == 400


async def test_401_raises_patent_auth_error_immediately(
    evidence_settings, fake_secrets, mock_uspto_401
):
    async with httpx.AsyncClient() as client:
        adapter = UsptoAdapter(evidence_settings, fake_secrets, client)
        with pytest.raises(PatentAuthError) as exc_info:
            await adapter.search("bad credentials", limit=5)
    assert exc_info.value.status_code == 401
    assert mock_uspto_401.calls.call_count == 1


async def test_403_raises_patent_auth_error_immediately(
    evidence_settings, fake_secrets, mock_uspto_403
):
    async with httpx.AsyncClient() as client:
        adapter = UsptoAdapter(evidence_settings, fake_secrets, client)
        with pytest.raises(PatentAuthError) as exc_info:
            await adapter.search("stale credential", limit=5)
    assert exc_info.value.status_code == 403
    assert isinstance(exc_info.value, PatentApiError)
    assert mock_uspto_403.calls.call_count == 1


async def test_500_retries_then_raises(evidence_settings, fake_secrets, mock_uspto_500):
    async with httpx.AsyncClient() as client:
        adapter = UsptoAdapter(evidence_settings, fake_secrets, client)
        with pytest.raises(PatentApiError) as exc_info:
            await adapter.search("q", limit=5)
    assert exc_info.value.status_code == 500
    # With max_retries=2, USPTO endpoint was called multiple times
    assert mock_uspto_500.calls.call_count >= 2


async def test_api_key_re_read_via_secrets_provider_on_each_search(
    evidence_settings, fake_secrets, mock_uspto
):
    async with httpx.AsyncClient() as client:
        adapter = UsptoAdapter(evidence_settings, fake_secrets, client)
        await adapter.search("q1", limit=2)
        await adapter.search("q2", limit=2)
    assert fake_secrets.call_count.get("USPTO_API_KEY", 0) == 2


async def test_api_key_rotation_picked_up_on_next_search(
    evidence_settings, fake_secrets, mock_uspto
):
    async with httpx.AsyncClient() as client:
        adapter = UsptoAdapter(evidence_settings, fake_secrets, client)
        await adapter.search("q1", limit=2)
        first_key = mock_uspto.calls[-1].request.headers["X-API-KEY"]

        fake_secrets._mapping["USPTO_API_KEY"] = "rotated-uspto-key"
        await adapter.search("q2", limit=2)
        second_key = mock_uspto.calls[-1].request.headers["X-API-KEY"]

    assert first_key == "test-uspto-key"
    assert second_key == "rotated-uspto-key"


async def test_single_dict_result_bag_maps_to_one_match(
    evidence_settings, fake_secrets, mock_uspto_single_dict
):
    async with httpx.AsyncClient() as client:
        adapter = UsptoAdapter(evidence_settings, fake_secrets, client)
        results = await adapter.search("sparse tensor quantization", limit=5)

    assert len(results) == 1
    assert results[0].reference == "US11234567"
    assert results[0].title == "Method for sparse tensor quantization"
    assert results[0].applicant == "Acme Corp"


async def test_list_valued_applicant_normalizes_to_first_name(
    evidence_settings, fake_secrets, mock_uspto_list_applicant
):
    async with httpx.AsyncClient() as client:
        adapter = UsptoAdapter(evidence_settings, fake_secrets, client)
        results = await adapter.search("sparse tensor quantization", limit=5)

    assert len(results) == 1
    assert results[0].applicant == "Acme Corp"


async def test_429_retry_after_waits_at_least_the_floor(
    evidence_settings, fake_secrets, mock_uspto_429_short_retry_after, monkeypatch
):
    sleep_mock = AsyncMock()
    monkeypatch.setattr(asyncio, "sleep", sleep_mock)

    async with httpx.AsyncClient() as client:
        adapter = UsptoAdapter(evidence_settings, fake_secrets, client)
        with pytest.raises(PatentApiError) as exc_info:
            await adapter.search("q", limit=5)

    assert exc_info.value.status_code == 429
    floor_waits = [
        call.args[0]
        for call in sleep_mock.await_args_list
        if call.args and call.args[0] == _USPTO_RETRY_AFTER_FLOOR_SECONDS
    ]
    assert floor_waits, "expected at least one sleep call floored at the minimum retry delay"


_ENRICH_HIT = {
    "applicationNumberText": "17123456",
    "applicationMetaData": {
        "inventionTitle": "Method for sparse tensor quantization",
        "firstApplicantName": "Acme Corp",
        "grantDate": "2024-03-15",
        "patentNumber": "11234567",
        "earliestPublicationNumber": "US11234567",
    },
}


def _search_response(hits):
    body = hits if isinstance(hits, list) else [hits]
    return httpx.Response(200, json={"count": len(body), "patentFileWrapperDataBag": body})


@pytest.fixture
def enrich_settings() -> EvidenceSettings:
    return EvidenceSettings(
        uspto_base=USPTO_BASE,
        patent_api_timeout_seconds=5.0,
        patent_api_max_retries=2,
        local_dev=True,
        uspto_api_key="test-uspto-key",
        uspto_enrich_top_n=5,
        model_router_url="http://test-model-router",
        vector_router_url="http://test-vector-router",
    )


def _mock_search_and_documents(*, abstract_payload, claims_payload):
    mock = respx.mock(base_url=USPTO_BASE, assert_all_called=False)
    mock.post("/api/v1/patent/applications/search").mock(return_value=_search_response(_ENRICH_HIT))
    mock.get("/api/v1/patent/applications/17123456/associated-documents").mock(
        return_value=abstract_payload
    )
    mock.get("/api/v1/patent/applications/17123456").mock(return_value=claims_payload)
    return mock


async def test_grant_abstract_populated_from_associated_documents(enrich_settings, fake_secrets):
    abstract_resp = httpx.Response(
        200,
        json={
            "patentFileWrapperDataBag": [
                {
                    "grantDocumentMetaData": {
                        "abstractText": "A method for quantizing sparse tensors.",
                        "inventionTitle": "Method for sparse tensor quantization",
                    }
                }
            ]
        },
    )
    claims_resp = httpx.Response(
        200,
        json={
            "patentFileWrapperDataBag": [
                {
                    "grantDocumentMetaData": {
                        "claimTextBag": [
                            {"claimText": "1. A method comprising quantizing."},
                            {"claimText": "2. The method of claim 1."},
                        ]
                    }
                }
            ]
        },
    )
    with _mock_search_and_documents(abstract_payload=abstract_resp, claims_payload=claims_resp):
        async with httpx.AsyncClient() as client:
            adapter = UsptoAdapter(enrich_settings, fake_secrets, client)
            results = await adapter.search("sparse tensor quantization", limit=5)

    assert len(results) == 1
    assert results[0].jurisdiction == "US"
    assert results[0].abstract == "A method for quantizing sparse tensors."
    assert results[0].claims == [
        "1. A method comprising quantizing.",
        "2. The method of claim 1.",
    ]


async def test_pending_application_falls_back_to_title_then_empty(enrich_settings, fake_secrets):
    abstract_resp = httpx.Response(404)
    claims_resp = httpx.Response(404)
    with _mock_search_and_documents(abstract_payload=abstract_resp, claims_payload=claims_resp):
        async with httpx.AsyncClient() as client:
            adapter = UsptoAdapter(enrich_settings, fake_secrets, client)
            results = await adapter.search("sparse tensor quantization", limit=5)

    assert len(results) == 1
    assert results[0].jurisdiction == "US"
    assert results[0].abstract == ""
    assert results[0].claims == []


async def test_claims_as_string_shape_normalizes_to_single_entry(enrich_settings, fake_secrets):
    abstract_resp = httpx.Response(200, json={"patentFileWrapperDataBag": []})
    claims_resp = httpx.Response(
        200,
        json={
            "patentFileWrapperDataBag": [
                {"grantDocumentMetaData": {"claimTextBag": "1. A single claim string."}}
            ]
        },
    )
    with _mock_search_and_documents(abstract_payload=abstract_resp, claims_payload=claims_resp):
        async with httpx.AsyncClient() as client:
            adapter = UsptoAdapter(enrich_settings, fake_secrets, client)
            results = await adapter.search("sparse tensor quantization", limit=5)

    assert results[0].claims == ["1. A single claim string."]


async def test_claims_as_dict_shape_normalizes_to_single_entry(enrich_settings, fake_secrets):
    abstract_resp = httpx.Response(200, json={"patentFileWrapperDataBag": []})
    claims_resp = httpx.Response(
        200,
        json={
            "patentFileWrapperDataBag": [
                {
                    "grantDocumentMetaData": {
                        "claimTextBag": {"claimText": "1. A single claim dict."}
                    }
                }
            ]
        },
    )
    with _mock_search_and_documents(abstract_payload=abstract_resp, claims_payload=claims_resp):
        async with httpx.AsyncClient() as client:
            adapter = UsptoAdapter(enrich_settings, fake_secrets, client)
            results = await adapter.search("sparse tensor quantization", limit=5)

    assert results[0].claims == ["1. A single claim dict."]


def test_parse_claims_bag_handles_string_dict_and_list_shapes():
    assert _parse_claims_bag("1. Text claim.") == ["1. Text claim."]
    assert _parse_claims_bag({"text": "1. Dict claim."}) == ["1. Dict claim."]
    assert _parse_claims_bag([{"claimText": "1. First."}, {"claimText": "2. Second."}]) == [
        "1. First.",
        "2. Second.",
    ]
    assert _parse_claims_bag(None) == []
    assert _parse_claims_bag([]) == []


async def test_enrichment_failure_is_non_fatal(enrich_settings, fake_secrets):
    mock = respx.mock(base_url=USPTO_BASE, assert_all_called=False)
    mock.post("/api/v1/patent/applications/search").mock(return_value=_search_response(_ENRICH_HIT))
    mock.get("/api/v1/patent/applications/17123456/associated-documents").mock(
        return_value=httpx.Response(500, text="Internal Server Error")
    )
    mock.get("/api/v1/patent/applications/17123456").mock(side_effect=httpx.ConnectError("boom"))
    with mock:
        async with httpx.AsyncClient() as client:
            adapter = UsptoAdapter(enrich_settings, fake_secrets, client)
            results = await adapter.search("sparse tensor quantization", limit=5)

    assert len(results) == 1
    assert results[0].abstract == ""
    assert results[0].claims == []


async def test_enrichment_capped_at_top_n(fake_secrets):
    settings = EvidenceSettings(
        uspto_base=USPTO_BASE,
        patent_api_timeout_seconds=5.0,
        patent_api_max_retries=2,
        local_dev=True,
        uspto_api_key="test-uspto-key",
        uspto_enrich_top_n=1,
        model_router_url="http://test-model-router",
        vector_router_url="http://test-vector-router",
    )
    second_hit = {
        "applicationNumberText": "17123457",
        "applicationMetaData": {
            "inventionTitle": "Neural network compression via pruning",
            "firstApplicantName": "TechLab Inc",
            "grantDate": "2024-01-20",
            "patentNumber": "11234568",
            "earliestPublicationNumber": "US11234568",
        },
    }
    mock = respx.mock(base_url=USPTO_BASE, assert_all_called=False)
    mock.post("/api/v1/patent/applications/search").mock(
        return_value=_search_response([_ENRICH_HIT, second_hit])
    )
    first_docs = mock.get("/api/v1/patent/applications/17123456/associated-documents").mock(
        return_value=httpx.Response(200, json={"patentFileWrapperDataBag": []})
    )
    first_claims = mock.get("/api/v1/patent/applications/17123456").mock(
        return_value=httpx.Response(200, json={"patentFileWrapperDataBag": []})
    )
    second_docs = mock.get("/api/v1/patent/applications/17123457/associated-documents").mock(
        return_value=httpx.Response(200, json={"patentFileWrapperDataBag": []})
    )
    second_claims = mock.get("/api/v1/patent/applications/17123457").mock(
        return_value=httpx.Response(200, json={"patentFileWrapperDataBag": []})
    )
    with mock:
        async with httpx.AsyncClient() as client:
            adapter = UsptoAdapter(settings, fake_secrets, client)
            results = await adapter.search("sparse tensor quantization", limit=5)
        first_docs_calls = first_docs.call_count
        first_claims_calls = first_claims.call_count
        second_docs_calls = second_docs.call_count
        second_claims_calls = second_claims.call_count

    assert len(results) == 2
    assert first_docs_calls == 1
    assert first_claims_calls == 1
    assert second_docs_calls == 0
    assert second_claims_calls == 0


async def test_adapter_constructs_rate_limiter_with_per_replica_rate(
    evidence_settings, fake_secrets
):
    async with httpx.AsyncClient() as client:
        adapter = UsptoAdapter(evidence_settings, fake_secrets, client)
    assert isinstance(adapter._rate_limiter, RateLimiter)
    assert adapter._rate_limiter._rate == evidence_settings.uspto_search_max_rps_per_replica


async def test_dispatch_request_acquires_rate_limiter_token(
    evidence_settings, fake_secrets, mock_uspto
):
    async with httpx.AsyncClient() as client:
        adapter = UsptoAdapter(evidence_settings, fake_secrets, client)
        mock_limiter = AsyncMock()
        adapter._rate_limiter = mock_limiter
        await adapter.search("q", limit=5)
    assert mock_limiter.acquire.call_count >= 1


async def test_dispatch_get_acquires_rate_limiter_token(enrich_settings, fake_secrets):
    abstract_resp = httpx.Response(200, json={"patentFileWrapperDataBag": []})
    claims_resp = httpx.Response(200, json={"patentFileWrapperDataBag": []})
    with _mock_search_and_documents(abstract_payload=abstract_resp, claims_payload=claims_resp):
        async with httpx.AsyncClient() as client:
            adapter = UsptoAdapter(enrich_settings, fake_secrets, client)
            mock_limiter = AsyncMock()
            adapter._rate_limiter = mock_limiter
            await adapter.search("sparse tensor quantization", limit=5)
    assert mock_limiter.acquire.call_count >= 3


async def test_enrichment_gets_issued_serially_not_via_gather(enrich_settings, fake_secrets):
    abstract_resp = httpx.Response(
        200,
        json={
            "patentFileWrapperDataBag": [
                {"grantDocumentMetaData": {"abstractText": "Test abstract."}}
            ]
        },
    )
    claims_resp = httpx.Response(
        200,
        json={
            "patentFileWrapperDataBag": [
                {"grantDocumentMetaData": {"claimTextBag": [{"claimText": "1. A claim."}]}}
            ]
        },
    )
    call_order = []

    async def tracked_get(url: str, headers=None, timeout=None):
        call_order.append(url)
        if "associated-documents" in url:
            return abstract_resp
        return claims_resp

    with _mock_search_and_documents(abstract_payload=abstract_resp, claims_payload=claims_resp):
        async with httpx.AsyncClient() as client:
            adapter = UsptoAdapter(enrich_settings, fake_secrets, client)
            adapter._client.get = tracked_get
            adapter._rate_limiter = AsyncMock(acquire=AsyncMock())
            await adapter.search("sparse tensor quantization", limit=5)

    enrich_calls = [url for url in call_order if "17123456" in url]
    assert len(enrich_calls) == 2
    assert "associated-documents" in enrich_calls[0]
    assert "associated-documents" not in enrich_calls[1]


@pytest.mark.skipif(
    not os.environ.get("USPTO_API_KEY"),
    reason="live USPTO ODP credentials not configured in this environment",
)
async def test_live_search_enriches_abstract_and_claims():
    settings = EvidenceSettings(
        uspto_api_key=os.environ["USPTO_API_KEY"],
        local_dev=True,
        model_router_url="http://test-model-router",
        vector_router_url="http://test-vector-router",
    )

    class _EnvSecrets:
        async def get_secret(self, name: str) -> str:
            return os.environ["USPTO_API_KEY"]

    async with httpx.AsyncClient() as client:
        adapter = UsptoAdapter(settings, _EnvSecrets(), client)
        results = await adapter.search("applicationMetaData.cpcClassificationBag:G06N*", limit=1)

    assert results
    assert results[0].jurisdiction == "US"
