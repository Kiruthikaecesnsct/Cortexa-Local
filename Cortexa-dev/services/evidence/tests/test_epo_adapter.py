import base64
import os
import time

import httpx
import pytest
import respx

from evidence.domain.enums.evidence_source import EvidenceSource
from evidence.domain.errors.evidence_errors import PatentApiError, PatentAuthError
from evidence.infrastructure.patent_apis.epo_adapter import EpoAdapter
from tests.conftest import (
    EPO_ABSTRACT_RESPONSE,
    EPO_BASE,
    EPO_BIBLIO_RESPONSE,
    EPO_BIBLIO_RESPONSE_BATCH2,
    EPO_BIBLIO_RESPONSE_SINGLE,
    EPO_CLAIMS_RESPONSE,
    EPO_HITS,
    EPO_TOKEN_RESPONSE,
)


async def test_first_call_fetches_token_and_returns_results(
    evidence_settings, fake_secrets, mock_epo_success
):
    async with httpx.AsyncClient() as client:
        adapter = EpoAdapter(evidence_settings, fake_secrets, client)
        results = await adapter.search("sparse tensor", limit=5)

    assert len(results) == 2
    assert results[0].reference == "EP3456789A1"
    assert results[0].title == "Sparse tensor encoding method"
    assert results[0].applicant == "EuroCorp GmbH"
    assert results[0].date == "2024-03-15"
    assert results[0].source is EvidenceSource.PatentApi


async def test_second_call_reuses_token(evidence_settings, fake_secrets, mock_epo_success):
    async with httpx.AsyncClient() as client:
        adapter = EpoAdapter(evidence_settings, fake_secrets, client)
        await adapter.search("sparse tensor method", limit=5)
        await adapter.search("neural network compression", limit=5)

    assert mock_epo_success.token.call_count == 1


async def test_expired_token_triggers_refresh(evidence_settings, fake_secrets, mock_epo_success):
    async with httpx.AsyncClient() as client:
        adapter = EpoAdapter(evidence_settings, fake_secrets, client)
        # Prime with a valid token then force expiry
        adapter._token = "old-token"
        adapter._token_expires_at = time.time() - 1.0  # already expired
        await adapter.search("sparse tensor method", limit=5)

    assert mock_epo_success.token.call_count == 1  # refreshed once


async def test_secret_rotation_forces_token_refresh_within_time_validity(
    evidence_settings, fake_secrets, mock_epo_success
):
    async with httpx.AsyncClient() as client:
        adapter = EpoAdapter(evidence_settings, fake_secrets, client)
        await adapter.search("sparse tensor method", limit=5)
        assert mock_epo_success.token.call_count == 1
        first_request = mock_epo_success.token.calls[-1].request
        first_credentials = base64.b64decode(
            first_request.headers["Authorization"].removeprefix("Basic ")
        ).decode()
        assert first_credentials == "test-consumer:test-secret"

        fake_secrets._mapping["EPO_CONSUMER_KEY"] = "rotated-consumer"
        fake_secrets._mapping["EPO_OAUTH_SECRET"] = "rotated-secret"

        await adapter.search("neural network compression", limit=5)

    assert mock_epo_success.token.call_count == 2
    second_request = mock_epo_success.token.calls[-1].request
    second_credentials = base64.b64decode(
        second_request.headers["Authorization"].removeprefix("Basic ")
    ).decode()
    assert second_credentials == "rotated-consumer:rotated-secret"


async def test_401_triggers_reauth_and_retry(evidence_settings, fake_secrets):
    call_count = {"search": 0}

    with respx.mock(base_url=evidence_settings.epo_base, assert_all_called=False) as mock:
        mock.post("/3.2/auth/accesstoken").mock(
            return_value=httpx.Response(200, json={"access_token": "new-token", "expires_in": 1200})
        )

        def _search_handler(_request):
            call_count["search"] += 1
            if call_count["search"] == 1:
                return httpx.Response(401, text="Unauthorized")
            return httpx.Response(
                200,
                json={
                    "ops:world-patent-data": {
                        "ops:biblio-search": {
                            "ops:search-result": {"ops:publication-reference": []}
                        }
                    }
                },
            )

        mock.get("/3.2/rest-services/published-data/search").mock(side_effect=_search_handler)
        biblio_route = mock.post("/3.2/rest-services/published-data/publication/docdb/biblio").mock(
            return_value=httpx.Response(200, json=EPO_BIBLIO_RESPONSE)
        )

        async with httpx.AsyncClient() as client:
            adapter = EpoAdapter(evidence_settings, fake_secrets, client)
            results = await adapter.search("sparse tensor method", limit=5)

    assert results == []
    assert call_count["search"] == 2  # first 401, then retry after re-auth
    assert biblio_route.call_count == 0  # no hits -> biblio short-circuited


async def test_second_401_raises_patent_auth_error(evidence_settings, fake_secrets):
    with respx.mock(base_url=evidence_settings.epo_base) as mock:
        mock.post("/3.2/auth/accesstoken").mock(
            return_value=httpx.Response(200, json={"access_token": "token", "expires_in": 1200})
        )
        mock.get("/3.2/rest-services/published-data/search").mock(
            return_value=httpx.Response(401, text="Unauthorized")
        )

        async with httpx.AsyncClient() as client:
            adapter = EpoAdapter(evidence_settings, fake_secrets, client)
            with pytest.raises(PatentAuthError):
                await adapter.search("sparse tensor method", limit=5)


async def test_500_retries_then_raises(evidence_settings, fake_secrets):
    with respx.mock(base_url=evidence_settings.epo_base) as mock:
        mock.post("/3.2/auth/accesstoken").mock(
            return_value=httpx.Response(200, json={"access_token": "token", "expires_in": 1200})
        )
        search_route = mock.get("/3.2/rest-services/published-data/search").mock(
            return_value=httpx.Response(500, text="Server Error")
        )

        async with httpx.AsyncClient() as client:
            adapter = EpoAdapter(evidence_settings, fake_secrets, client)
            with pytest.raises(PatentApiError) as exc_info:
                await adapter.search("sparse tensor method", limit=5)

    assert exc_info.value.status_code == 500
    assert search_route.call_count >= 2


async def test_biblio_500_retries_then_raises(evidence_settings, fake_secrets):
    with respx.mock(base_url=evidence_settings.epo_base) as mock:
        mock.post("/3.2/auth/accesstoken").mock(
            return_value=httpx.Response(200, json={"access_token": "token", "expires_in": 1200})
        )
        mock.get("/3.2/rest-services/published-data/search").mock(
            return_value=httpx.Response(
                200,
                json={
                    "ops:world-patent-data": {
                        "ops:biblio-search": {
                            "ops:search-result": {
                                "ops:publication-reference": [
                                    {
                                        "document-id": {
                                            "country": {"$": "EP"},
                                            "doc-number": {"$": "3456789"},
                                            "kind": {"$": "A1"},
                                        }
                                    }
                                ]
                            }
                        }
                    }
                },
            )
        )
        biblio_route = mock.post("/3.2/rest-services/published-data/publication/docdb/biblio").mock(
            return_value=httpx.Response(500, text="Server Error")
        )

        async with httpx.AsyncClient() as client:
            adapter = EpoAdapter(evidence_settings, fake_secrets, client)
            with pytest.raises(PatentApiError) as exc_info:
                await adapter.search("sparse tensor method", limit=5)

    assert exc_info.value.status_code == 500
    assert biblio_route.call_count >= 2


async def test_all_fields_mapped(evidence_settings, fake_secrets, mock_epo_success):
    async with httpx.AsyncClient() as client:
        adapter = EpoAdapter(evidence_settings, fake_secrets, client)
        results = await adapter.search("sparse tensor", limit=5)

    r = results[0]
    assert r.reference
    assert r.title
    assert r.applicant
    assert r.date
    assert r.url
    assert 0.0 <= r.relevance_score <= 1.0


async def test_docdb_document_id_preferred_over_epodoc(
    evidence_settings, fake_secrets, mock_epo_success
):
    async with httpx.AsyncClient() as client:
        adapter = EpoAdapter(evidence_settings, fake_secrets, client)
        results = await adapter.search("sparse tensor", limit=5)

    assert results[0].reference == "EP3456789A1"


async def test_plain_document_id_fields_parsed(evidence_settings, fake_secrets, mock_epo_success):
    async with httpx.AsyncClient() as client:
        adapter = EpoAdapter(evidence_settings, fake_secrets, client)
        results = await adapter.search("sparse tensor", limit=5)

    assert results[1].reference == "US20260141387A1"
    assert results[1].applicant == "NorthPeak Robotics"


async def test_en_title_preferred_over_non_en(evidence_settings, fake_secrets, mock_epo_success):
    async with httpx.AsyncClient() as client:
        adapter = EpoAdapter(evidence_settings, fake_secrets, client)
        results = await adapter.search("sparse tensor", limit=5)

    assert results[0].title == "Sparse tensor encoding method"


async def test_empty_search_results_short_circuits_biblio_call(
    evidence_settings, fake_secrets, mock_epo_empty_search
):
    async with httpx.AsyncClient() as client:
        adapter = EpoAdapter(evidence_settings, fake_secrets, client)
        results = await adapter.search("q", limit=5)

    assert results == []
    assert mock_epo_empty_search.biblio.call_count == 0


async def test_references_are_unique_across_distinct_hits(
    evidence_settings, fake_secrets, mock_epo_success
):
    async with httpx.AsyncClient() as client:
        adapter = EpoAdapter(evidence_settings, fake_secrets, client)
        results = await adapter.search("sparse tensor", limit=5)

    references = [r.reference for r in results]
    assert len(references) == len(set(references))


async def test_biblio_reauth_token_propagates_to_next_batch(evidence_settings, fake_secrets):
    refs = [f"EP.{1000000 + i}.A1" for i in range(150)]

    with respx.mock(base_url=EPO_BASE, assert_all_called=False) as mock:
        token_route = mock.post("/3.2/auth/accesstoken").mock(
            return_value=httpx.Response(200, json=EPO_TOKEN_RESPONSE)
        )

        def _biblio_handler(request: httpx.Request) -> httpx.Response:
            if request.headers.get("Authorization") == "Bearer old-token":
                return httpx.Response(401, text="Unauthorized")
            batch_refs = request.content.decode().splitlines()
            if len(batch_refs) == 100:
                return httpx.Response(200, json=EPO_BIBLIO_RESPONSE)
            return httpx.Response(200, json=EPO_BIBLIO_RESPONSE_BATCH2)

        biblio_route = mock.post("/3.2/rest-services/published-data/publication/docdb/biblio").mock(
            side_effect=_biblio_handler
        )

        async with httpx.AsyncClient() as client:
            adapter = EpoAdapter(evidence_settings, fake_secrets, client)
            documents = await adapter._fetch_biblio_documents("old-token", refs)

    assert len(documents) == 3
    assert token_route.call_count == 1
    assert biblio_route.call_count == 3


async def test_jurisdiction_populated_from_biblio(
    evidence_settings, fake_secrets, mock_epo_success
):
    async with httpx.AsyncClient() as client:
        adapter = EpoAdapter(evidence_settings, fake_secrets, client)
        results = await adapter.search("sparse tensor", limit=5)

    assert results[0].jurisdiction == "EP"
    assert results[1].jurisdiction == "US"


async def test_abstract_and_claims_enriched_when_missing_from_biblio(
    evidence_settings, fake_secrets, mock_epo_success
):
    async with httpx.AsyncClient() as client:
        adapter = EpoAdapter(evidence_settings, fake_secrets, client)
        results = await adapter.search("sparse tensor", limit=5)

    assert results[0].abstract == "A method for encoding sparse tensors."
    assert results[0].claims == [
        "1. A method comprising encoding a tensor.",
        "2. The method of claim 1, further comprising.",
    ]
    assert mock_epo_success.abstract.call_count == 2
    assert mock_epo_success.claims.call_count == 2


async def test_abstract_404_leaves_field_empty(evidence_settings, fake_secrets):
    with respx.mock(base_url=EPO_BASE) as mock:
        mock.post("/3.2/auth/accesstoken").mock(
            return_value=httpx.Response(200, json=EPO_TOKEN_RESPONSE)
        )
        mock.get("/3.2/rest-services/published-data/search").mock(
            return_value=httpx.Response(
                200,
                json={
                    "ops:world-patent-data": {
                        "ops:biblio-search": {
                            "ops:search-result": {"ops:publication-reference": [EPO_HITS[0]]}
                        }
                    }
                },
            )
        )
        mock.post("/3.2/rest-services/published-data/publication/docdb/biblio").mock(
            return_value=httpx.Response(200, json=EPO_BIBLIO_RESPONSE_SINGLE)
        )
        mock.get(url__regex=r".*/publication/docdb/.+/abstract$").mock(
            return_value=httpx.Response(404, text="Not Found")
        )
        claims_route = mock.get(url__regex=r".*/publication/docdb/.+/claims$").mock(
            return_value=httpx.Response(200, json=EPO_CLAIMS_RESPONSE)
        )

        async with httpx.AsyncClient() as client:
            adapter = EpoAdapter(evidence_settings, fake_secrets, client)
            results = await adapter.search("sparse tensor", limit=5)

    assert results[0].abstract == ""
    assert results[0].claims
    assert claims_route.call_count == 1


async def test_claims_404_leaves_field_empty(evidence_settings, fake_secrets):
    with respx.mock(base_url=EPO_BASE) as mock:
        mock.post("/3.2/auth/accesstoken").mock(
            return_value=httpx.Response(200, json=EPO_TOKEN_RESPONSE)
        )
        mock.get("/3.2/rest-services/published-data/search").mock(
            return_value=httpx.Response(
                200,
                json={
                    "ops:world-patent-data": {
                        "ops:biblio-search": {
                            "ops:search-result": {"ops:publication-reference": [EPO_HITS[0]]}
                        }
                    }
                },
            )
        )
        mock.post("/3.2/rest-services/published-data/publication/docdb/biblio").mock(
            return_value=httpx.Response(200, json=EPO_BIBLIO_RESPONSE_SINGLE)
        )
        abstract_route = mock.get(url__regex=r".*/publication/docdb/.+/abstract$").mock(
            return_value=httpx.Response(200, json=EPO_ABSTRACT_RESPONSE)
        )
        mock.get(url__regex=r".*/publication/docdb/.+/claims$").mock(
            return_value=httpx.Response(404, text="Not Found")
        )

        async with httpx.AsyncClient() as client:
            adapter = EpoAdapter(evidence_settings, fake_secrets, client)
            results = await adapter.search("sparse tensor", limit=5)

    assert results[0].claims == []
    assert abstract_route.call_count == 1


async def test_malformed_abstract_body_leaves_field_empty(evidence_settings, fake_secrets):
    with respx.mock(base_url=EPO_BASE) as mock:
        mock.post("/3.2/auth/accesstoken").mock(
            return_value=httpx.Response(200, json=EPO_TOKEN_RESPONSE)
        )
        mock.get("/3.2/rest-services/published-data/search").mock(
            return_value=httpx.Response(
                200,
                json={
                    "ops:world-patent-data": {
                        "ops:biblio-search": {
                            "ops:search-result": {"ops:publication-reference": [EPO_HITS[0]]}
                        }
                    }
                },
            )
        )
        mock.post("/3.2/rest-services/published-data/publication/docdb/biblio").mock(
            return_value=httpx.Response(200, json=EPO_BIBLIO_RESPONSE_SINGLE)
        )
        mock.get(url__regex=r".*/publication/docdb/.+/abstract$").mock(
            return_value=httpx.Response(200, json=["unexpected", "shape"])
        )
        claims_route = mock.get(url__regex=r".*/publication/docdb/.+/claims$").mock(
            return_value=httpx.Response(200, json=EPO_CLAIMS_RESPONSE)
        )

        async with httpx.AsyncClient() as client:
            adapter = EpoAdapter(evidence_settings, fake_secrets, client)
            results = await adapter.search("sparse tensor", limit=5)

    assert len(results) == 1
    assert results[0].abstract == ""
    assert results[0].claims
    assert claims_route.call_count == 1


async def test_enrichment_bounded_to_top_n(evidence_settings, fake_secrets):
    bounded_settings = evidence_settings.model_copy(update={"epo_enrich_top_n": 1})

    with respx.mock(base_url=EPO_BASE) as mock:
        mock.post("/3.2/auth/accesstoken").mock(
            return_value=httpx.Response(200, json=EPO_TOKEN_RESPONSE)
        )
        mock.get("/3.2/rest-services/published-data/search").mock(
            return_value=httpx.Response(
                200,
                json={
                    "ops:world-patent-data": {
                        "ops:biblio-search": {
                            "ops:search-result": {"ops:publication-reference": EPO_HITS}
                        }
                    }
                },
            )
        )
        mock.post("/3.2/rest-services/published-data/publication/docdb/biblio").mock(
            return_value=httpx.Response(200, json=EPO_BIBLIO_RESPONSE)
        )
        abstract_route = mock.get(url__regex=r".*/publication/docdb/.+/abstract$").mock(
            return_value=httpx.Response(200, json=EPO_ABSTRACT_RESPONSE)
        )
        claims_route = mock.get(url__regex=r".*/publication/docdb/.+/claims$").mock(
            return_value=httpx.Response(200, json=EPO_CLAIMS_RESPONSE)
        )

        async with httpx.AsyncClient() as client:
            adapter = EpoAdapter(bounded_settings, fake_secrets, client)
            results = await adapter.search("sparse tensor", limit=5)

    assert len(results) == 2
    assert results[0].claims
    assert results[1].claims == []
    assert abstract_route.call_count == 1
    assert claims_route.call_count == 1


@pytest.mark.skipif(
    not (os.environ.get("EPO_CONSUMER_KEY") and os.environ.get("EPO_OAUTH_SECRET")),
    reason="live EPO OPS credentials not configured in this environment",
)
async def test_live_search_enriches_abstract_and_claims():
    from evidence.infrastructure.config.settings import EvidenceSettings

    settings = EvidenceSettings(
        epo_consumer_key=os.environ["EPO_CONSUMER_KEY"],
        epo_oauth_secret=os.environ["EPO_OAUTH_SECRET"],
        local_dev=True,
        model_router_url="http://test-model-router",
        vector_router_url="http://test-vector-router",
    )

    class _EnvSecrets:
        async def get_secret(self, name: str) -> str:
            return os.environ[name]

    async with httpx.AsyncClient() as client:
        adapter = EpoAdapter(settings, _EnvSecrets(), client)
        results = await adapter.search("sparse tensor quantization", limit=1)

    assert results
    assert results[0].jurisdiction
