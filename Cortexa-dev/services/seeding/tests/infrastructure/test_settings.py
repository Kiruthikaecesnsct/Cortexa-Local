import pytest
from pydantic import ValidationError

from seeding.infrastructure.config.settings import SeedingSettings

_MODEL_ROUTER_URL = "http://cortexa-dev-model-router.internal.example.azurecontainerapps.io"
_VECTOR_ROUTER_URL = "http://cortexa-dev-vector-router.internal.example.azurecontainerapps.io"


def test_model_router_url_binds_to_env_var(monkeypatch):
    monkeypatch.setenv("MODEL_ROUTER_URL", _MODEL_ROUTER_URL)
    s = SeedingSettings()
    assert s.model_router_url == _MODEL_ROUTER_URL


def test_valid_settings_accepted():
    s = SeedingSettings(model_router_url=_MODEL_ROUTER_URL)
    assert s.model_router_url == _MODEL_ROUTER_URL


def test_missing_model_router_url_raises(monkeypatch):
    monkeypatch.delenv("MODEL_ROUTER_URL", raising=False)
    with pytest.raises(ValidationError) as exc_info:
        SeedingSettings()
    assert "model_router_url" in str(exc_info.value)


def test_blank_model_router_url_raises():
    with pytest.raises(ValidationError) as exc_info:
        SeedingSettings(model_router_url="")
    assert "model_router_url" in str(exc_info.value)


def test_whitespace_only_model_router_url_raises():
    with pytest.raises(ValidationError) as exc_info:
        SeedingSettings(model_router_url="   ")
    assert "model_router_url" in str(exc_info.value)


def test_vector_router_url_binds_to_env_var(monkeypatch):
    monkeypatch.setenv("MODEL_ROUTER_URL", _MODEL_ROUTER_URL)
    monkeypatch.setenv("VECTOR_ROUTER_URL", _VECTOR_ROUTER_URL)
    s = SeedingSettings()
    assert s.vector_router_url == _VECTOR_ROUTER_URL


def test_missing_vector_router_url_raises(monkeypatch):
    monkeypatch.delenv("VECTOR_ROUTER_URL", raising=False)
    with pytest.raises(ValidationError) as exc_info:
        SeedingSettings(model_router_url=_MODEL_ROUTER_URL)
    assert "vector_router_url" in str(exc_info.value)


def test_blank_vector_router_url_raises():
    with pytest.raises(ValidationError) as exc_info:
        SeedingSettings(model_router_url=_MODEL_ROUTER_URL, vector_router_url="")
    assert "vector_router_url" in str(exc_info.value)
