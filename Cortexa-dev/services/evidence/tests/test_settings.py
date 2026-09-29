import pytest
from pydantic import ValidationError

from evidence.infrastructure.config.settings import EvidenceSettings

_MODEL_ROUTER_URL = "http://cortexa-dev-model-router.internal.example.azurecontainerapps.io"
_VECTOR_ROUTER_URL = "http://cortexa-dev-vector-router.internal.example.azurecontainerapps.io"

_VALID = dict(
    model_router_url=_MODEL_ROUTER_URL,
    vector_router_url=_VECTOR_ROUTER_URL,
)


def test_valid_settings_accepted():
    s = EvidenceSettings(**_VALID)
    assert s.model_router_url == _MODEL_ROUTER_URL
    assert s.vector_router_url == _VECTOR_ROUTER_URL


def test_missing_model_router_url_raises():
    with pytest.raises(ValidationError) as exc_info:
        EvidenceSettings(vector_router_url=_VECTOR_ROUTER_URL)
    assert "model_router_url" in str(exc_info.value)


def test_missing_vector_router_url_raises():
    with pytest.raises(ValidationError) as exc_info:
        EvidenceSettings(model_router_url=_MODEL_ROUTER_URL)
    assert "vector_router_url" in str(exc_info.value)


def test_blank_model_router_url_raises():
    with pytest.raises(ValidationError) as exc_info:
        EvidenceSettings(model_router_url="", vector_router_url=_VECTOR_ROUTER_URL)
    assert "model_router_url" in str(exc_info.value)


def test_blank_vector_router_url_raises():
    with pytest.raises(ValidationError) as exc_info:
        EvidenceSettings(model_router_url=_MODEL_ROUTER_URL, vector_router_url="")
    assert "vector_router_url" in str(exc_info.value)


def test_whitespace_only_model_router_url_raises():
    with pytest.raises(ValidationError) as exc_info:
        EvidenceSettings(model_router_url="   ", vector_router_url=_VECTOR_ROUTER_URL)
    assert "model_router_url" in str(exc_info.value)


def test_whitespace_only_vector_router_url_raises():
    with pytest.raises(ValidationError) as exc_info:
        EvidenceSettings(model_router_url=_MODEL_ROUTER_URL, vector_router_url="   ")
    assert "vector_router_url" in str(exc_info.value)


# ---------------------------------------------------------------------------
# BUG140: candidate-deadline budget invariant
#
# deadline >= llm_acquire_timeout + model_router_call_timeout(patent_research) + headroom
#
# The single-mode deadline previously violated this (120 < 65 + 60 = 125), letting the
# candidate deadline fire before the LLM call it was waiting on could return, which the
# scheduler's error classifier then mis-reported as a transient bare TimeoutError.
# ---------------------------------------------------------------------------


def test_candidate_deadline_seconds_satisfies_llm_budget_invariant():
    s = EvidenceSettings(**_VALID)

    minimum_safe_deadline = (
        s.evidence_llm_acquire_timeout_seconds + s.model_router_patent_research_call_timeout_seconds
    )

    assert s.evidence_candidate_deadline_seconds >= minimum_safe_deadline


def test_candidate_deadline_defaults_match_documented_budget():
    EXPECTED_SINGLE_DEADLINE = 150.0

    s = EvidenceSettings(**_VALID)

    assert s.evidence_candidate_deadline_seconds == EXPECTED_SINGLE_DEADLINE


def test_uspto_rate_fields_present_with_documented_defaults():
    s = EvidenceSettings(**_VALID)
    assert s.uspto_search_ceiling_rps == 4.0
    assert s.uspto_search_target_rps == 3.0
    assert s.uspto_search_max_rps_per_replica == 0.3


def test_uspto_enrich_top_n_defaults_to_three():
    s = EvidenceSettings(**_VALID)
    assert s.uspto_enrich_top_n == 3
