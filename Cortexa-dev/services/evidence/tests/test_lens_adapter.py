import json
import os

import httpx
import pytest
import respx

from evidence.domain.enums.evidence_source import EvidenceSource
from evidence.domain.errors.evidence_errors import PatentApiError
from evidence.infrastructure.patent_apis.lens_adapter import (
    LensAdapter,
    _extract_reference,
)

_EXPECTED_QUERY_STRING_FIELDS = [
    "biblio.invention_title.text",
    "abstract",
    "claims.claim",
]

_LENS_HIT_ENRICHED = {
    "lens_id": "400-007-840-114-661",
    "biblio": {
        "invention_title": [{"text": "Adaptive gradient checkpointing", "lang": "en"}],
        "parties": {"applicants": [{"extracted_name": {"value": "NovaTech Inc"}}]},
        "publication_reference": {
            "jurisdiction": "US",
            "doc_number": "11999999",
            "kind": "B2",
            "date": "2024-06-01",
        },
    },
    "date_published": "2024-06-01",
    "abstract": [{"text": "A method for reducing memory usage during training.", "lang": "en"}],
    "claims": [
        {"claim_text": "1. A method comprising: allocating a checkpoint buffer.", "lang": "en"},
        {"claim_text": "2. The method of claim 1, wherein the buffer is reused.", "lang": "en"},
    ],
}


async def test_success_returns_patent_matches(evidence_settings, fake_secrets_with_lens, mock_lens):
    async with httpx.AsyncClient() as client:
        adapter = LensAdapter(evidence_settings, fake_secrets_with_lens, client)
        results = await adapter.search("sparse tensor quantization", limit=5)

    assert len(results) == 2
    assert results[0].reference == "US11234567"
    assert results[0].title == "Method for sparse tensor quantization"
    assert results[0].applicant == "Acme Corp"
    assert results[0].date == "2024-03-15"
    assert "100-004-910-081-338" in results[0].url
    assert results[0].source is EvidenceSource.PatentApi


async def test_relevance_scores_in_bounds(evidence_settings, fake_secrets_with_lens, mock_lens):
    async with httpx.AsyncClient() as client:
        adapter = LensAdapter(evidence_settings, fake_secrets_with_lens, client)
        results = await adapter.search("q", limit=5)
    for r in results:
        assert 0.0 <= r.relevance_score <= 1.0


async def test_empty_results_returns_empty_list(
    evidence_settings, fake_secrets_with_lens, mock_lens_empty
):
    async with httpx.AsyncClient() as client:
        adapter = LensAdapter(evidence_settings, fake_secrets_with_lens, client)
        results = await adapter.search("nonexistent topic", limit=5)
    assert results == []


async def test_400_raises_patent_api_error_immediately(
    evidence_settings, fake_secrets_with_lens, mock_lens_400
):
    async with httpx.AsyncClient() as client:
        adapter = LensAdapter(evidence_settings, fake_secrets_with_lens, client)
        with pytest.raises(PatentApiError) as exc_info:
            await adapter.search("bad query", limit=5)
    assert exc_info.value.status_code == 400


async def test_500_retries_then_raises(evidence_settings, fake_secrets_with_lens, mock_lens_500):
    async with httpx.AsyncClient() as client:
        adapter = LensAdapter(evidence_settings, fake_secrets_with_lens, client)
        with pytest.raises(PatentApiError) as exc_info:
            await adapter.search("q", limit=5)
    assert exc_info.value.status_code == 500
    assert mock_lens_500.calls.call_count >= 2


async def test_api_key_re_read_via_secrets_provider_on_each_search(
    evidence_settings, fake_secrets_with_lens, mock_lens
):
    async with httpx.AsyncClient() as client:
        adapter = LensAdapter(evidence_settings, fake_secrets_with_lens, client)
        await adapter.search("q1", limit=2)
        await adapter.search("q2", limit=2)
    assert fake_secrets_with_lens.call_count.get("LENS_API_KEY", 0) == 2


async def test_api_key_rotation_picked_up_on_next_search(
    evidence_settings, fake_secrets_with_lens, mock_lens
):
    async with httpx.AsyncClient() as client:
        adapter = LensAdapter(evidence_settings, fake_secrets_with_lens, client)
        await adapter.search("q1", limit=2)
        first_auth = mock_lens.calls[-1].request.headers["Authorization"]

        fake_secrets_with_lens._mapping["LENS_API_KEY"] = "rotated-lens-key"
        await adapter.search("q2", limit=2)
        second_auth = mock_lens.calls[-1].request.headers["Authorization"]

    assert first_auth == "Bearer test-lens-key"
    assert second_auth == "Bearer rotated-lens-key"


async def test_list_shaped_publication_reference_returns_matches(
    evidence_settings, fake_secrets_with_lens, mock_lens_list_reference
):
    async with httpx.AsyncClient() as client:
        adapter = LensAdapter(evidence_settings, fake_secrets_with_lens, client)
        results = await adapter.search("distributed cache coherence", limit=5)

    assert len(results) == 1
    assert results[0].reference == "US11987654"
    assert results[0].title == "Distributed cache coherence protocol"
    assert results[0].applicant == "DataSys LLC"


def test_extract_reference_with_dict_publication_reference_does_not_raise():
    biblio = {
        "publication_reference": {
            "jurisdiction": "US",
            "doc_number": "12050595",
            "kind": "B1",
            "date": "2024-07-30",
        }
    }

    result = _extract_reference(biblio)

    assert result == "US12050595"


def test_extract_reference_with_list_publication_reference():
    biblio = {
        "publication_reference": [
            {"jurisdiction": "US", "doc_number": "11987654", "kind": "A1"},
            {"jurisdiction": "US", "doc_number": "11987655", "kind": "B1"},
        ]
    }

    result = _extract_reference(biblio)

    assert result == "US11987654"


def test_extract_reference_with_no_publication_reference_returns_empty_string():
    assert _extract_reference({}) == ""


def test_extract_reference_with_missing_doc_number_returns_empty_string():
    biblio = {"publication_reference": {"jurisdiction": "US"}}

    assert _extract_reference(biblio) == ""


async def test_maps_abstract_claims_and_jurisdiction(evidence_settings, fake_secrets_with_lens):
    with respx.mock(base_url="http://test-lens") as mock:
        mock.post("/patent/search").mock(
            return_value=httpx.Response(200, json={"total": 1, "data": [_LENS_HIT_ENRICHED]})
        )
        async with httpx.AsyncClient() as client:
            adapter = LensAdapter(evidence_settings, fake_secrets_with_lens, client)
            results = await adapter.search("gradient checkpointing", limit=5)

    assert len(results) == 1
    result = results[0]
    assert result.jurisdiction == "US"
    assert result.abstract == "A method for reducing memory usage during training."
    assert result.claims == [
        "1. A method comprising: allocating a checkpoint buffer.",
        "2. The method of claim 1, wherein the buffer is reused.",
    ]


async def test_missing_enrichment_fields_default_to_empty(
    evidence_settings, fake_secrets_with_lens, mock_lens
):
    async with httpx.AsyncClient() as client:
        adapter = LensAdapter(evidence_settings, fake_secrets_with_lens, client)
        results = await adapter.search("sparse tensor quantization", limit=5)

    assert results[0].abstract == ""
    assert results[0].claims == []
    assert results[0].jurisdiction == "US"


async def test_query_string_fields_use_schema_valid_paths(
    evidence_settings, fake_secrets_with_lens
):
    with respx.mock(base_url="http://test-lens") as mock:
        route = mock.post("/patent/search").mock(
            return_value=httpx.Response(200, json={"total": 0, "data": []})
        )
        async with httpx.AsyncClient() as client:
            adapter = LensAdapter(evidence_settings, fake_secrets_with_lens, client)
            await adapter.search("gradient checkpointing", limit=5)

    sent_payload = route.calls.last.request.content
    body = json.loads(sent_payload)
    fields = body["query"]["query_string"]["fields"]
    assert fields == _EXPECTED_QUERY_STRING_FIELDS
    for bare_field in ("title", "claims", "abstract.text"):
        if bare_field != "abstract":
            assert bare_field not in fields


@pytest.mark.skipif(not os.environ.get("LENS_API_KEY"), reason="live LENS_API_KEY not configured")
async def test_live_lens_search_returns_matches(evidence_settings):
    class _EnvSecrets:
        async def get_secret(self, name: str) -> str:
            return os.environ[name]

    live_settings = evidence_settings.model_copy(update={"lens_base": "https://api.lens.org"})
    async with httpx.AsyncClient() as client:
        adapter = LensAdapter(live_settings, _EnvSecrets(), client)
        results = await adapter.search("sparse tensor quantization", limit=3)

    assert isinstance(results, list)
    for result in results:
        assert result.reference
        assert 0.0 <= result.relevance_score <= 1.0
