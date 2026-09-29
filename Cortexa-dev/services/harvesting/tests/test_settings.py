import pytest
from pydantic import ValidationError

from harvesting.infrastructure.config.settings import HarvestingSettings

_VALID = dict(
    cosmos_uri="https://test.documents.azure.com:443/",
    servicebus_namespace_fqdn="test.servicebus.windows.net",
)


def test_valid_settings_accepted():
    s = HarvestingSettings(**_VALID)
    assert s.cosmos_uri == _VALID["cosmos_uri"]
    assert s.servicebus_namespace_fqdn == _VALID["servicebus_namespace_fqdn"]


def test_missing_cosmos_uri_raises():
    with pytest.raises(ValidationError) as exc_info:
        HarvestingSettings(servicebus_namespace_fqdn="test.servicebus.windows.net")
    assert "cosmos_uri" in str(exc_info.value)


def test_missing_servicebus_namespace_fqdn_raises():
    with pytest.raises(ValidationError) as exc_info:
        HarvestingSettings(cosmos_uri="https://test.documents.azure.com:443/")
    assert "servicebus_namespace_fqdn" in str(exc_info.value)


def test_blank_cosmos_uri_raises():
    with pytest.raises(ValidationError) as exc_info:
        HarvestingSettings(
            cosmos_uri="",
            servicebus_namespace_fqdn="test.servicebus.windows.net",
        )
    assert "cosmos_uri" in str(exc_info.value)


def test_blank_servicebus_namespace_fqdn_raises():
    with pytest.raises(ValidationError) as exc_info:
        HarvestingSettings(
            cosmos_uri="https://test.documents.azure.com:443/",
            servicebus_namespace_fqdn="",
        )
    assert "servicebus_namespace_fqdn" in str(exc_info.value)


def test_whitespace_only_cosmos_uri_raises():
    with pytest.raises(ValidationError) as exc_info:
        HarvestingSettings(
            cosmos_uri="   ",
            servicebus_namespace_fqdn="test.servicebus.windows.net",
        )
    assert "cosmos_uri" in str(exc_info.value)


def test_whitespace_only_servicebus_namespace_fqdn_raises():
    with pytest.raises(ValidationError) as exc_info:
        HarvestingSettings(
            cosmos_uri="https://test.documents.azure.com:443/",
            servicebus_namespace_fqdn="  ",
        )
    assert "servicebus_namespace_fqdn" in str(exc_info.value)
