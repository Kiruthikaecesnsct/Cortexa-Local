from unittest.mock import AsyncMock, MagicMock, patch

import pytest

from evidence.domain.errors.evidence_errors import SecretResolutionError
from evidence.infrastructure.config.secrets_provider import SecretsProvider
from evidence.infrastructure.config.settings import EvidenceSettings


def _local_settings(**kwargs) -> EvidenceSettings:
    base = dict(
        local_dev=True,
        uspto_api_key="my-uspto-key",
        epo_consumer_key="my-consumer",
        epo_oauth_secret="my-oauth-secret",
        model_router_url="http://test-model-router",
        vector_router_url="http://test-vector-router",
    )
    base.update(kwargs)
    return EvidenceSettings(**base)


async def test_local_dev_resolves_from_settings():
    provider = SecretsProvider(_local_settings())
    assert await provider.get_secret("USPTO_API_KEY") == "my-uspto-key"
    assert await provider.get_secret("EPO_CONSUMER_KEY") == "my-consumer"
    assert await provider.get_secret("EPO_OAUTH_SECRET") == "my-oauth-secret"


async def test_local_dev_caches_secret():
    provider = SecretsProvider(_local_settings())
    val1 = await provider.get_secret("USPTO_API_KEY")
    val2 = await provider.get_secret("USPTO_API_KEY")
    assert val1 == val2 == "my-uspto-key"
    assert list(provider._cache.keys()).count("USPTO_API_KEY") == 1


async def test_local_dev_empty_value_raises():
    settings = _local_settings(uspto_api_key="")
    provider = SecretsProvider(settings)
    with pytest.raises(SecretResolutionError) as exc_info:
        await provider.get_secret("USPTO_API_KEY")
    assert "USPTO_API_KEY" in str(exc_info.value)


async def test_local_dev_unknown_name_raises():
    provider = SecretsProvider(_local_settings())
    with pytest.raises(SecretResolutionError):
        await provider.get_secret("UNKNOWN_SECRET")


async def test_keyvault_path_calls_secret_client():
    settings = EvidenceSettings(
        local_dev=False,
        keyvault_uri="https://test-vault.vault.azure.net/",
        model_router_url="http://test-model-router",
        vector_router_url="http://test-vector-router",
    )
    mock_credential = MagicMock()
    provider = SecretsProvider(settings, credential=mock_credential)
    mock_secret = MagicMock()
    mock_secret.value = "vault-value"

    mock_inner = AsyncMock()
    mock_inner.get_secret.return_value = mock_secret

    mock_ctx = AsyncMock()
    mock_ctx.__aenter__.return_value = mock_inner

    with patch(
        "evidence.infrastructure.config.secrets_provider.SecretClient",
        return_value=mock_ctx,
    ):
        value = await provider.get_secret("USPTO_API_KEY")

    assert value == "vault-value"
    mock_inner.get_secret.assert_called_once_with("uspto-api-key")


async def test_keyvault_no_credential_raises():
    settings = EvidenceSettings(
        local_dev=False,
        keyvault_uri="https://test-vault.vault.azure.net/",
        model_router_url="http://test-model-router",
        vector_router_url="http://test-vector-router",
    )
    provider = SecretsProvider(settings)
    with pytest.raises(SecretResolutionError):
        await provider.get_secret("USPTO_API_KEY")


async def test_invalidate_on_version_change_first_call_does_not_clear_cache():
    provider = SecretsProvider(_local_settings())
    await provider.get_secret("USPTO_API_KEY")

    provider.invalidate_on_version_change(1)

    assert "USPTO_API_KEY" in provider._cache


async def test_invalidate_on_version_change_same_version_keeps_cache():
    provider = SecretsProvider(_local_settings())
    await provider.get_secret("USPTO_API_KEY")
    provider.invalidate_on_version_change(1)

    provider.invalidate_on_version_change(1)

    assert "USPTO_API_KEY" in provider._cache


async def test_invalidate_on_version_change_bumps_version_clears_cache():
    provider = SecretsProvider(_local_settings())
    await provider.get_secret("USPTO_API_KEY")
    provider.invalidate_on_version_change(1)
    assert "USPTO_API_KEY" in provider._cache

    provider.invalidate_on_version_change(2)

    assert provider._cache == {}


async def test_invalidate_on_version_change_refetches_updated_secret():
    provider = SecretsProvider(_local_settings(uspto_api_key="key-v1"))
    first = await provider.get_secret("USPTO_API_KEY")
    provider.invalidate_on_version_change(1)
    assert first == "key-v1"

    provider._settings.uspto_api_key = "key-v2"
    provider.invalidate_on_version_change(2)
    second = await provider.get_secret("USPTO_API_KEY")

    assert second == "key-v2"


async def test_keyvault_exception_raises_secret_resolution_error():
    settings = EvidenceSettings(
        local_dev=False,
        keyvault_uri="https://test-vault.vault.azure.net/",
        model_router_url="http://test-model-router",
        vector_router_url="http://test-vector-router",
    )
    mock_credential = MagicMock()
    provider = SecretsProvider(settings, credential=mock_credential)

    mock_inner = AsyncMock()
    mock_inner.get_secret.side_effect = RuntimeError("network error")

    mock_ctx = AsyncMock()
    mock_ctx.__aenter__.return_value = mock_inner

    with patch(
        "evidence.infrastructure.config.secrets_provider.SecretClient",
        return_value=mock_ctx,
    ):
        with pytest.raises(SecretResolutionError):
            await provider.get_secret("USPTO_API_KEY")
