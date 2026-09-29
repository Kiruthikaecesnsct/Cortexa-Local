import pytest
from pydantic import ValidationError

from scoring.infrastructure.config.settings import ScoringSettings

_MODEL_ROUTER_URL = "http://cortexa-dev-model-router.internal.example.azurecontainerapps.io"

_VALID = dict(
    cosmos_uri="https://test.documents.azure.com:443/",
    servicebus_namespace_fqdn="test.servicebus.windows.net",
    model_router_url=_MODEL_ROUTER_URL,
)


def test_valid_settings_accepted():
    s = ScoringSettings(**_VALID)
    assert s.cosmos_uri == _VALID["cosmos_uri"]
    assert s.servicebus_namespace_fqdn == _VALID["servicebus_namespace_fqdn"]
    assert s.model_router_url == _MODEL_ROUTER_URL


def test_scoring_requested_topic_default():
    s = ScoringSettings(**_VALID)
    assert s.scoring_requested_topic == "scoring-requested"


def test_scoring_failed_topic_default():
    s = ScoringSettings(**_VALID)
    assert s.scoring_failed_topic == "scoring-failed"


def test_scoring_subscription_default():
    s = ScoringSettings(**_VALID)
    assert s.scoring_subscription == "scoring"


def test_consumer_max_attempts_default():
    s = ScoringSettings(**_VALID)
    assert s.consumer_max_attempts == 5


def test_missing_cosmos_uri_raises():
    with pytest.raises(ValidationError) as exc_info:
        ScoringSettings(
            servicebus_namespace_fqdn="test.servicebus.windows.net",
            model_router_url=_MODEL_ROUTER_URL,
        )
    assert "cosmos_uri" in str(exc_info.value)


def test_missing_servicebus_fqdn_raises():
    with pytest.raises(ValidationError) as exc_info:
        ScoringSettings(
            cosmos_uri="https://test.documents.azure.com:443/",
            model_router_url=_MODEL_ROUTER_URL,
        )
    assert "servicebus_namespace_fqdn" in str(exc_info.value)


def test_missing_model_router_url_raises():
    with pytest.raises(ValidationError) as exc_info:
        ScoringSettings(
            cosmos_uri="https://test.documents.azure.com:443/",
            servicebus_namespace_fqdn="test.servicebus.windows.net",
        )
    assert "model_router_url" in str(exc_info.value)


def test_blank_cosmos_uri_raises():
    with pytest.raises(ValidationError) as exc_info:
        ScoringSettings(
            cosmos_uri="",
            servicebus_namespace_fqdn="test.servicebus.windows.net",
            model_router_url=_MODEL_ROUTER_URL,
        )
    assert "cosmos_uri" in str(exc_info.value)


def test_blank_servicebus_fqdn_raises():
    with pytest.raises(ValidationError) as exc_info:
        ScoringSettings(
            cosmos_uri="https://test.documents.azure.com:443/",
            servicebus_namespace_fqdn="",
            model_router_url=_MODEL_ROUTER_URL,
        )
    assert "servicebus_namespace_fqdn" in str(exc_info.value)


def test_blank_model_router_url_raises():
    with pytest.raises(ValidationError) as exc_info:
        ScoringSettings(
            cosmos_uri="https://test.documents.azure.com:443/",
            servicebus_namespace_fqdn="test.servicebus.windows.net",
            model_router_url="",
        )
    assert "model_router_url" in str(exc_info.value)


def test_whitespace_only_cosmos_uri_raises():
    with pytest.raises(ValidationError) as exc_info:
        ScoringSettings(
            cosmos_uri="   ",
            servicebus_namespace_fqdn="test.servicebus.windows.net",
            model_router_url=_MODEL_ROUTER_URL,
        )
    assert "cosmos_uri" in str(exc_info.value)


def test_whitespace_only_servicebus_fqdn_raises():
    with pytest.raises(ValidationError) as exc_info:
        ScoringSettings(
            cosmos_uri="https://test.documents.azure.com:443/",
            servicebus_namespace_fqdn="  ",
            model_router_url=_MODEL_ROUTER_URL,
        )
    assert "servicebus_namespace_fqdn" in str(exc_info.value)


def test_whitespace_only_model_router_url_raises():
    with pytest.raises(ValidationError) as exc_info:
        ScoringSettings(
            cosmos_uri="https://test.documents.azure.com:443/",
            servicebus_namespace_fqdn="test.servicebus.windows.net",
            model_router_url="   ",
        )
    assert "model_router_url" in str(exc_info.value)
