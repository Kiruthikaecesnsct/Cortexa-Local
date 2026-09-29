import time
from unittest.mock import AsyncMock

import pytest
from azure.cosmos.exceptions import CosmosResourceNotFoundError

from evidence.domain.enums.patent_source_name import PatentSourceName
from evidence.infrastructure.config.patent_config_provider import PatentConfigProvider


@pytest.fixture
def mock_container():
    return AsyncMock()


@pytest.fixture
def provider(mock_container):
    return PatentConfigProvider(mock_container, cache_ttl_seconds=30.0)


def _doc(sources: dict, config_version: int = 3) -> dict:
    return {
        "id": "patent-sources",
        "batch_id": "global",
        "sources": sources,
        "config_version": config_version,
        "updated_at": "2026-07-13T00:00:00Z",
    }


async def test_all_sources_enabled(mock_container, provider):
    mock_container.read_item.return_value = _doc(
        {
            "uspto": {"enabled": True},
            "epo": {"enabled": True},
            "lens": {"enabled": True},
        }
    )

    config = await provider.get_config()

    assert config.enabled == frozenset(
        {PatentSourceName.USPTO, PatentSourceName.EPO, PatentSourceName.Lens}
    )
    assert config.version == 3


async def test_lens_disabled_excluded_from_enabled_set(mock_container, provider):
    mock_container.read_item.return_value = _doc(
        {
            "uspto": {"enabled": True},
            "epo": {"enabled": True},
            "lens": {"enabled": False},
        }
    )

    config = await provider.get_config()

    assert config.enabled == frozenset({PatentSourceName.USPTO, PatentSourceName.EPO})
    assert PatentSourceName.Lens not in config.enabled


async def test_two_sources_disabled_leaves_one_enabled(mock_container, provider):
    mock_container.read_item.return_value = _doc(
        {
            "uspto": {"enabled": True},
            "epo": {"enabled": False},
            "lens": {"enabled": False},
        }
    )

    config = await provider.get_config()

    assert config.enabled == frozenset({PatentSourceName.USPTO})


async def test_all_sources_disabled_yields_empty_enabled_set(mock_container, provider):
    mock_container.read_item.return_value = _doc(
        {
            "uspto": {"enabled": False},
            "epo": {"enabled": False},
            "lens": {"enabled": False},
        }
    )

    config = await provider.get_config()

    assert config.enabled == frozenset()


async def test_missing_source_key_in_doc_defaults_to_enabled(mock_container, provider):
    mock_container.read_item.return_value = _doc({"uspto": {"enabled": False}})

    config = await provider.get_config()

    assert PatentSourceName.USPTO not in config.enabled
    assert PatentSourceName.EPO in config.enabled
    assert PatentSourceName.Lens in config.enabled


async def test_doc_not_found_defaults_to_all_enabled_version_zero(mock_container, provider):
    mock_container.read_item.side_effect = CosmosResourceNotFoundError()

    config = await provider.get_config()

    assert config.enabled == frozenset(
        {PatentSourceName.USPTO, PatentSourceName.EPO, PatentSourceName.Lens}
    )
    assert config.version == 0


async def test_generic_fetch_error_defaults_to_all_enabled(mock_container, provider):
    mock_container.read_item.side_effect = RuntimeError("cosmos unavailable")

    config = await provider.get_config()

    assert config.enabled == frozenset(
        {PatentSourceName.USPTO, PatentSourceName.EPO, PatentSourceName.Lens}
    )
    assert config.version == 0


async def test_config_cached_within_ttl(mock_container, monkeypatch):
    mock_container.read_item.return_value = _doc(
        {
            "uspto": {"enabled": True},
            "epo": {"enabled": True},
            "lens": {"enabled": True},
        }
    )

    start = 1000.0
    current_time = start

    def mock_monotonic():
        return current_time

    monkeypatch.setattr(time, "monotonic", mock_monotonic)

    provider = PatentConfigProvider(mock_container, cache_ttl_seconds=30.0)

    await provider.get_config()
    current_time = start + 10.0
    await provider.get_config()

    mock_container.read_item.assert_called_once()


async def test_config_refetched_after_ttl_expires(mock_container, monkeypatch):
    mock_container.read_item.return_value = _doc(
        {
            "uspto": {"enabled": True},
            "epo": {"enabled": True},
            "lens": {"enabled": True},
        }
    )

    start = 1000.0
    current_time = start

    def mock_monotonic():
        return current_time

    monkeypatch.setattr(time, "monotonic", mock_monotonic)

    provider = PatentConfigProvider(mock_container, cache_ttl_seconds=30.0)

    await provider.get_config()
    current_time = start + 31.0
    await provider.get_config()

    assert mock_container.read_item.call_count == 2


async def test_config_version_change_reflected_after_ttl_expiry(mock_container, monkeypatch):
    start = 1000.0
    current_time = start

    def mock_monotonic():
        return current_time

    monkeypatch.setattr(time, "monotonic", mock_monotonic)

    provider = PatentConfigProvider(mock_container, cache_ttl_seconds=30.0)

    mock_container.read_item.return_value = _doc(
        {
            "uspto": {"enabled": True},
            "epo": {"enabled": True},
            "lens": {"enabled": True},
        },
        config_version=1,
    )
    first = await provider.get_config()
    assert first.version == 1

    mock_container.read_item.return_value = _doc(
        {
            "uspto": {"enabled": True},
            "epo": {"enabled": True},
            "lens": {"enabled": False},
        },
        config_version=2,
    )
    current_time = start + 31.0
    second = await provider.get_config()

    assert second.version == 2
    assert PatentSourceName.Lens not in second.enabled


async def test_read_item_called_with_frozen_contract_id_and_partition(mock_container, provider):
    mock_container.read_item.return_value = _doc(
        {
            "uspto": {"enabled": True},
            "epo": {"enabled": True},
            "lens": {"enabled": True},
        }
    )

    await provider.get_config()

    mock_container.read_item.assert_awaited_once_with(item="patent-sources", partition_key="global")
