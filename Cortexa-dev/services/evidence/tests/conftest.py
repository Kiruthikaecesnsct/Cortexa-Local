import json
from types import SimpleNamespace

import httpx
import pytest
import respx

from evidence.infrastructure.config.settings import EvidenceSettings

USPTO_BASE = "http://test-uspto"
EPO_BASE = "http://test-epo"
LENS_BASE = "http://test-lens"

USPTO_HITS = [
    {
        "applicationNumberText": "17123456",
        "applicationMetaData": {
            "inventionTitle": "Method for sparse tensor quantization",
            "firstApplicantName": "Acme Corp",
            "grantDate": "2024-03-15",
            "patentNumber": "11234567",
            "earliestPublicationNumber": "US11234567",
        },
    },
    {
        "applicationNumberText": "17123457",
        "applicationMetaData": {
            "inventionTitle": "Neural network compression via pruning",
            "firstApplicantName": "TechLab Inc",
            "grantDate": "2024-01-20",
            "patentNumber": "11234568",
            "earliestPublicationNumber": "US11234568",
        },
    },
]

EPO_TOKEN_RESPONSE = {
    "access_token": "test-epo-token-abc",
    "expires_in": 1200,
    "token_type": "Bearer",
}

LENS_HITS = [
    {
        "lens_id": "100-004-910-081-338",
        "biblio": {
            "invention_title": [{"text": "Method for sparse tensor quantization", "lang": "en"}],
            "parties": {"applicants": [{"extracted_name": {"value": "Acme Corp"}}]},
            "publication_reference": {
                "jurisdiction": "US",
                "doc_number": "11234567",
                "kind": "B1",
                "date": "2024-03-15",
            },
        },
        "date_published": "2024-03-15",
    },
    {
        "lens_id": "200-005-820-092-449",
        "biblio": {
            "invention_title": [{"text": "Neural network compression via pruning", "lang": "en"}],
            "parties": {"applicants": [{"extracted_name": {"value": "TechLab Inc"}}]},
            "publication_reference": {
                "jurisdiction": "US",
                "doc_number": "11234568",
                "kind": "B1",
                "date": "2024-01-20",
            },
        },
        "date_published": "2024-01-20",
    },
]

LENS_HIT_LIST_REFERENCE = {
    "lens_id": "300-006-730-103-550",
    "biblio": {
        "invention_title": [{"text": "Distributed cache coherence protocol", "lang": "en"}],
        "parties": {"applicants": [{"extracted_name": {"value": "DataSys LLC"}}]},
        "publication_reference": [
            {"jurisdiction": "US", "doc_number": "11987654", "kind": "A1", "date": "2023-11-02"},
            {"jurisdiction": "US", "doc_number": "11987655", "kind": "B1", "date": "2024-02-10"},
        ],
    },
    "date_published": "2023-11-02",
}

EPO_HITS = [
    {
        "@system": "ops",
        "@family-id": "12345678",
        "document-id": [
            {
                "@document-id-type": "docdb",
                "country": {"$": "EP"},
                "doc-number": {"$": "3456789"},
                "kind": {"$": "A1"},
            },
            {
                "@document-id-type": "epodoc",
                "country": {"$": "EP"},
                "doc-number": {"$": "3456789A1"},
            },
        ],
    },
    {
        "@system": "ops",
        "@family-id": "87654321",
        "document-id": {
            "country": {"$": "US"},
            "doc-number": {"$": "20260141387"},
            "kind": {"$": "A1"},
        },
    },
]

EPO_BIBLIO_RESPONSE = {
    "exchange-documents": {
        "exchange-document": [
            {
                "bibliographic-data": {
                    "publication-reference": {
                        "document-id": [
                            {
                                "@document-id-type": "docdb",
                                "country": {"$": "EP"},
                                "doc-number": {"$": "3456789"},
                                "kind": {"$": "A1"},
                                "date": {"$": "20240315"},
                            }
                        ]
                    },
                    "invention-title": [
                        {"@lang": "en", "$": "Sparse tensor encoding method"},
                        {"@lang": "de", "$": "Verfahren zur Kodierung dünnbesetzter Tensoren"},
                    ],
                    "parties": {
                        "applicants": {
                            "applicant": [{"applicant-name": {"name": {"$": "EuroCorp GmbH"}}}]
                        }
                    },
                }
            },
            {
                "bibliographic-data": {
                    "publication-reference": {
                        "document-id": [
                            {
                                "@document-id-type": "docdb",
                                "country": {"$": "US"},
                                "doc-number": {"$": "20260141387"},
                                "kind": {"$": "A1"},
                                "date": {"$": "20260220"},
                            }
                        ]
                    },
                    "invention-title": [{"@lang": "en", "$": "Distributed tensor compaction"}],
                    "parties": {
                        "applicants": {
                            "applicant": [{"applicant-name": {"name": {"$": "NorthPeak Robotics"}}}]
                        }
                    },
                }
            },
        ]
    }
}


EPO_ABSTRACT_RESPONSE = {
    "ops:world-patent-data": {
        "exchange-documents": {
            "exchange-document": {
                "abstract": [
                    {"@lang": "en", "p": {"$": "A method for encoding sparse tensors."}},
                    {
                        "@lang": "de",
                        "p": {"$": "Ein Verfahren zur Kodierung dünnbesetzter Tensoren."},
                    },
                ]
            }
        }
    }
}

EPO_CLAIMS_RESPONSE = {
    "ops:world-patent-data": {
        "ftxt:fulltext-documents": {
            "ftxt:fulltext-document": {
                "claims": [
                    {
                        "@lang": "en",
                        "claim": [
                            {"claim-text": {"$": "1. A method comprising encoding a tensor."}},
                            {"claim-text": {"$": "2. The method of claim 1, further comprising."}},
                        ],
                    }
                ]
            }
        }
    }
}


EPO_BIBLIO_RESPONSE_SINGLE = {
    "exchange-documents": {
        "exchange-document": EPO_BIBLIO_RESPONSE["exchange-documents"]["exchange-document"][0]
    }
}


EPO_BIBLIO_RESPONSE_BATCH2 = {
    "exchange-documents": {
        "exchange-document": [
            {
                "bibliographic-data": {
                    "publication-reference": {
                        "document-id": [
                            {
                                "@document-id-type": "docdb",
                                "country": {"$": "EP"},
                                "doc-number": {"$": "4000001"},
                                "kind": {"$": "A1"},
                                "date": {"$": "20260115"},
                            }
                        ]
                    },
                    "invention-title": [{"@lang": "en", "$": "Batched sparse tensor pipeline"}],
                    "parties": {
                        "applicants": {
                            "applicant": [{"applicant-name": {"name": {"$": "SecondBatch Ltd"}}}]
                        }
                    },
                }
            }
        ]
    }
}


@pytest.fixture
def evidence_settings() -> EvidenceSettings:
    return EvidenceSettings(
        uspto_base=USPTO_BASE,
        epo_base=EPO_BASE,
        epo_token_path="/3.2/auth/accesstoken",
        lens_base=LENS_BASE,
        patent_api_timeout_seconds=5.0,
        patent_api_max_retries=2,
        local_dev=True,
        uspto_api_key="test-uspto-key",
        epo_consumer_key="test-consumer",
        epo_oauth_secret="test-secret",
        lens_api_key="test-lens-key",
        model_router_url=MODEL_ROUTER_URL,
        vector_router_url=VECTOR_ROUTER_URL,
    )


class _FakeSecrets:
    def __init__(self, mapping: dict[str, str]) -> None:
        self._mapping = mapping
        self.call_count: dict[str, int] = {}

    async def get_secret(self, name: str) -> str:
        self.call_count[name] = self.call_count.get(name, 0) + 1
        if name not in self._mapping:
            from evidence.domain.errors.evidence_errors import SecretResolutionError

            raise SecretResolutionError(name, "not found in fake mapping")
        return self._mapping[name]


@pytest.fixture
def fake_secrets() -> _FakeSecrets:
    return _FakeSecrets(
        {
            "USPTO_API_KEY": "test-uspto-key",
            "EPO_CONSUMER_KEY": "test-consumer",
            "EPO_OAUTH_SECRET": "test-secret",
            "LENS_API_KEY": "test-lens-key",
        }
    )


@pytest.fixture
def fake_secrets_with_lens() -> _FakeSecrets:
    return _FakeSecrets({"LENS_API_KEY": "test-lens-key"})


@pytest.fixture
def mock_uspto():
    with respx.mock(base_url=USPTO_BASE) as mock:
        mock.post("/api/v1/patent/applications/search").mock(
            return_value=httpx.Response(
                200, json={"count": len(USPTO_HITS), "patentFileWrapperDataBag": USPTO_HITS}
            )
        )
        yield mock


@pytest.fixture
def mock_uspto_single_dict():
    with respx.mock(base_url=USPTO_BASE) as mock:
        mock.post("/api/v1/patent/applications/search").mock(
            return_value=httpx.Response(
                200, json={"count": 1, "patentFileWrapperDataBag": USPTO_HITS[0]}
            )
        )
        yield mock


@pytest.fixture
def mock_uspto_list_applicant():
    hit = json.loads(json.dumps(USPTO_HITS[0]))
    hit["applicationMetaData"]["firstApplicantName"] = ["Acme Corp", "Acme Subsidiary LLC"]
    with respx.mock(base_url=USPTO_BASE) as mock:
        mock.post("/api/v1/patent/applications/search").mock(
            return_value=httpx.Response(200, json={"count": 1, "patentFileWrapperDataBag": [hit]})
        )
        yield mock


@pytest.fixture
def mock_uspto_empty():
    with respx.mock(base_url=USPTO_BASE) as mock:
        mock.post("/api/v1/patent/applications/search").mock(
            return_value=httpx.Response(200, json={"count": 0, "patentFileWrapperDataBag": []})
        )
        yield mock


@pytest.fixture
def mock_uspto_500():
    with respx.mock(base_url=USPTO_BASE) as mock:
        mock.post("/api/v1/patent/applications/search").mock(
            return_value=httpx.Response(500, text="Internal Server Error")
        )
        yield mock


@pytest.fixture
def mock_uspto_400():
    with respx.mock(base_url=USPTO_BASE) as mock:
        mock.post("/api/v1/patent/applications/search").mock(
            return_value=httpx.Response(400, json={"error": "bad request"})
        )
        yield mock


@pytest.fixture
def mock_uspto_401():
    with respx.mock(base_url=USPTO_BASE) as mock:
        mock.post("/api/v1/patent/applications/search").mock(
            return_value=httpx.Response(401, json={"error": "unauthorized"})
        )
        yield mock


@pytest.fixture
def mock_uspto_403():
    with respx.mock(base_url=USPTO_BASE) as mock:
        mock.post("/api/v1/patent/applications/search").mock(
            return_value=httpx.Response(403, json={"error": "forbidden"})
        )
        yield mock


@pytest.fixture
def mock_uspto_429_short_retry_after():
    with respx.mock(base_url=USPTO_BASE) as mock:
        mock.post("/api/v1/patent/applications/search").mock(
            return_value=httpx.Response(
                429,
                headers={"Retry-After": "1"},
                json={"code": 429, "error": "Too Many Requests"},
            )
        )
        yield mock


@pytest.fixture
def mock_epo_success():
    with respx.mock(base_url=EPO_BASE) as mock:
        token_route = mock.post("/3.2/auth/accesstoken").mock(
            return_value=httpx.Response(200, json=EPO_TOKEN_RESPONSE)
        )
        search_route = mock.get("/3.2/rest-services/published-data/search").mock(
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
        biblio_route = mock.post("/3.2/rest-services/published-data/publication/docdb/biblio").mock(
            return_value=httpx.Response(200, json=EPO_BIBLIO_RESPONSE)
        )
        abstract_route = mock.get(url__regex=r".*/publication/docdb/.+/abstract$").mock(
            return_value=httpx.Response(200, json=EPO_ABSTRACT_RESPONSE)
        )
        claims_route = mock.get(url__regex=r".*/publication/docdb/.+/claims$").mock(
            return_value=httpx.Response(200, json=EPO_CLAIMS_RESPONSE)
        )
        yield SimpleNamespace(
            token=token_route,
            search=search_route,
            biblio=biblio_route,
            abstract=abstract_route,
            claims=claims_route,
        )


@pytest.fixture
def mock_epo_empty_search():
    with respx.mock(base_url=EPO_BASE, assert_all_called=False) as mock:
        token_route = mock.post("/3.2/auth/accesstoken").mock(
            return_value=httpx.Response(200, json=EPO_TOKEN_RESPONSE)
        )
        search_route = mock.get("/3.2/rest-services/published-data/search").mock(
            return_value=httpx.Response(
                200,
                json={
                    "ops:world-patent-data": {
                        "ops:biblio-search": {
                            "ops:search-result": {"ops:publication-reference": []}
                        }
                    }
                },
            )
        )
        biblio_route = mock.post("/3.2/rest-services/published-data/publication/docdb/biblio").mock(
            return_value=httpx.Response(200, json=EPO_BIBLIO_RESPONSE)
        )
        yield SimpleNamespace(token=token_route, search=search_route, biblio=biblio_route)


@pytest.fixture
def mock_lens():
    with respx.mock(base_url=LENS_BASE) as mock:
        mock.post("/patent/search").mock(
            return_value=httpx.Response(200, json={"total": 2, "data": LENS_HITS})
        )
        yield mock


@pytest.fixture
def mock_lens_list_reference():
    with respx.mock(base_url=LENS_BASE) as mock:
        mock.post("/patent/search").mock(
            return_value=httpx.Response(200, json={"total": 1, "data": [LENS_HIT_LIST_REFERENCE]})
        )
        yield mock


@pytest.fixture
def mock_lens_empty():
    with respx.mock(base_url=LENS_BASE) as mock:
        mock.post("/patent/search").mock(
            return_value=httpx.Response(200, json={"total": 0, "data": []})
        )
        yield mock


@pytest.fixture
def mock_lens_500():
    with respx.mock(base_url=LENS_BASE) as mock:
        mock.post("/patent/search").mock(
            return_value=httpx.Response(500, text="Internal Server Error")
        )
        yield mock


@pytest.fixture
def mock_lens_400():
    with respx.mock(base_url=LENS_BASE) as mock:
        mock.post("/patent/search").mock(
            return_value=httpx.Response(400, json={"error": "bad request"})
        )
        yield mock


VECTOR_ROUTER_URL = "http://test-vector-router"

_EMBED_RESPONSE = {"embeddings": [[0.1, 0.2, 0.3]]}

_SEARCH_HITS = {
    "hits": [
        {
            "id": "EP1234567",
            "score": 0.92,
            "payload": {
                "title": "Sparse tensor method",
                "applicant": "CorpusCorp",
                "date": "2024-01-10",
                "url": "https://corpus/EP1234567",
            },
        },
        {
            "id": "US9876543",
            "score": 0.75,
            "payload": {
                "title": "Neural compression",
                "applicant": "AI Labs",
                "date": "2023-05-20",
                "url": "https://corpus/US9876543",
            },
        },
    ]
}


@pytest.fixture
def corpus_settings() -> EvidenceSettings:
    return EvidenceSettings(
        vector_router_url=VECTOR_ROUTER_URL,
        model_router_url=MODEL_ROUTER_URL,
        corpus_search_timeout_seconds=5.0,
        corpus_max_retries=2,
        corpus_max_concurrency=2,
    )


@pytest.fixture
def mock_vector_router():
    with respx.mock(base_url=VECTOR_ROUTER_URL) as mock:
        mock.post("/embed").mock(return_value=httpx.Response(200, json=_EMBED_RESPONSE))
        mock.post("/search").mock(return_value=httpx.Response(200, json=_SEARCH_HITS))
        yield mock


@pytest.fixture
def mock_vector_router_empty():
    with respx.mock(base_url=VECTOR_ROUTER_URL) as mock:
        mock.post("/embed").mock(return_value=httpx.Response(200, json=_EMBED_RESPONSE))
        mock.post("/search").mock(return_value=httpx.Response(200, json={"hits": []}))
        yield mock


@pytest.fixture
def mock_vector_router_500():
    with respx.mock(base_url=VECTOR_ROUTER_URL) as mock:
        mock.post("/embed").mock(return_value=httpx.Response(200, json=_EMBED_RESPONSE))
        mock.post("/search").mock(return_value=httpx.Response(500, text="Internal Server Error"))
        yield mock


@pytest.fixture
def mock_vector_router_400():
    with respx.mock(base_url=VECTOR_ROUTER_URL) as mock:
        mock.post("/embed").mock(return_value=httpx.Response(200, json=_EMBED_RESPONSE))
        mock.post("/search").mock(return_value=httpx.Response(400, json={"error": "bad request"}))
        yield mock


# ---------------------------------------------------------------------------
# Corpus loader fixtures
# ---------------------------------------------------------------------------


@pytest.fixture
def corpus_load_settings() -> EvidenceSettings:
    return EvidenceSettings(
        vector_router_url=VECTOR_ROUTER_URL,
        model_router_url=MODEL_ROUTER_URL,
        corpus_load_timeout_seconds=5.0,
        corpus_load_max_retries=2,
        corpus_load_batch_size=2,
        corpus_max_concurrency=2,
    )


@pytest.fixture
def mock_vector_router_load():
    with respx.mock(base_url=VECTOR_ROUTER_URL, assert_all_called=False) as mock:
        mock.post("/embed").mock(
            side_effect=lambda request: httpx.Response(
                200,
                json={"embeddings": [[0.1, 0.2, 0.3] for _ in _embed_texts(request)]},
            )
        )
        mock.post("/upsert").mock(
            side_effect=lambda request: httpx.Response(
                200,
                json={"upserted": len(_upsert_items(request))},
            )
        )
        yield mock


def _embed_texts(request: httpx.Request) -> list[str]:
    return json.loads(request.content).get("texts", [])


def _upsert_items(request: httpx.Request) -> list[dict]:
    return json.loads(request.content).get("items", [])


@pytest.fixture
def mock_vector_router_load_500():
    with respx.mock(base_url=VECTOR_ROUTER_URL) as mock:
        mock.post("/embed").mock(return_value=httpx.Response(200, json=_EMBED_RESPONSE))
        mock.post("/upsert").mock(return_value=httpx.Response(500, text="Internal Server Error"))
        yield mock


# ---------------------------------------------------------------------------
# LLM Research fixtures
# ---------------------------------------------------------------------------

MODEL_ROUTER_URL = "http://test-model-router"

_SINGLE_SUCCESS_BODY = {
    "provider": "foundry",
    "model": "gpt-4o",
    "content": (
        '{"findings": ["US11111111: related neural net"], "confidence": 0.82,'
        ' "citations": ["US11111111"]}'
    ),
    "citations": None,
    "usage": None,
    "grounding": None,
    "error": None,
}


@pytest.fixture
def llm_research_settings() -> EvidenceSettings:
    return EvidenceSettings(
        model_router_url=MODEL_ROUTER_URL,
        vector_router_url=VECTOR_ROUTER_URL,
        llm_research_timeout_seconds=5.0,
        llm_research_max_retries=2,
        llm_research_max_concurrency=2,
    )


@pytest.fixture
def mock_model_router():
    with respx.mock(base_url=MODEL_ROUTER_URL) as mock:
        mock.post("/complete").mock(return_value=httpx.Response(200, json=_SINGLE_SUCCESS_BODY))
        yield mock


@pytest.fixture
def mock_model_router_500():
    with respx.mock(base_url=MODEL_ROUTER_URL) as mock:
        mock.post("/complete").mock(return_value=httpx.Response(500, text="Internal Server Error"))
        yield mock


@pytest.fixture
def mock_model_router_400():
    with respx.mock(base_url=MODEL_ROUTER_URL) as mock:
        mock.post("/complete").mock(return_value=httpx.Response(400, json={"error": "bad request"}))
        yield mock


@pytest.fixture
def mock_model_router_error_in_body():
    with respx.mock(base_url=MODEL_ROUTER_URL) as mock:
        mock.post("/complete").mock(
            return_value=httpx.Response(
                200,
                json={
                    "provider": "foundry",
                    "model": "gpt-4o",
                    "content": '{"findings": [], "confidence": 0.0, "citations": []}',
                    "citations": None,
                    "usage": None,
                    "grounding": None,
                    "error": "quota exceeded",
                },
            )
        )
        yield mock


@pytest.fixture
def mock_model_router_bad_json():
    with respx.mock(base_url=MODEL_ROUTER_URL) as mock:
        mock.post("/complete").mock(
            return_value=httpx.Response(
                200,
                json={
                    "provider": "foundry",
                    "model": "gpt-4o",
                    "content": "not valid json",
                    "citations": None,
                    "usage": None,
                    "grounding": None,
                    "error": None,
                },
            )
        )
        yield mock


@pytest.fixture
def mock_model_router_markdown_json():
    with respx.mock(base_url=MODEL_ROUTER_URL) as mock:
        mock.post("/complete").mock(
            return_value=httpx.Response(
                200,
                json={
                    "provider": "foundry",
                    "model": "gpt-4o",
                    "content": (
                        '```json\n{"findings": ["X"], "confidence": 0.5, "citations": []}\n```'
                    ),
                    "citations": None,
                    "usage": None,
                    "grounding": None,
                    "error": None,
                },
            )
        )
        yield mock
