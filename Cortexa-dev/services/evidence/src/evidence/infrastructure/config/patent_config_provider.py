import logging
import time
from dataclasses import dataclass

from azure.cosmos.aio import ContainerProxy
from azure.cosmos.exceptions import CosmosResourceNotFoundError

from evidence.domain.enums.patent_source_name import PatentSourceName

logger = logging.getLogger(__name__)

_CONFIG_DOC_ID = "patent-sources"
_CONFIG_PARTITION_KEY = "global"
_DEFAULT_VERSION = 0

_SOURCE_KEY_TO_NAME: dict[str, PatentSourceName] = {
    "uspto": PatentSourceName.USPTO,
    "epo": PatentSourceName.EPO,
    "lens": PatentSourceName.Lens,
}


@dataclass(frozen=True)
class PatentSourceConfig:
    enabled: frozenset[PatentSourceName]
    version: int


_ALL_SOURCES_ENABLED = PatentSourceConfig(
    enabled=frozenset(_SOURCE_KEY_TO_NAME.values()),
    version=_DEFAULT_VERSION,
)


def _parse_config_doc(doc: dict) -> PatentSourceConfig:
    sources = doc.get("sources") or {}
    version = int(doc.get("config_version", _DEFAULT_VERSION))
    enabled = frozenset(
        name
        for key, name in _SOURCE_KEY_TO_NAME.items()
        if bool((sources.get(key) or {}).get("enabled", True))
    )
    return PatentSourceConfig(enabled=enabled, version=version)


class PatentConfigProvider:
    def __init__(self, container: ContainerProxy, cache_ttl_seconds: float) -> None:
        self._container = container
        self._cache_ttl_seconds = cache_ttl_seconds
        self._cached: PatentSourceConfig | None = None
        self._expires_at: float = 0.0

    async def get_config(self) -> PatentSourceConfig:
        cached = self._read_cache()
        if cached is not None:
            return cached

        config = await self._fetch_config()
        self._write_cache(config)
        return config

    def _read_cache(self) -> PatentSourceConfig | None:
        if self._cached is None:
            return None
        if time.monotonic() < self._expires_at:
            return self._cached
        return None

    def _write_cache(self, config: PatentSourceConfig) -> None:
        self._cached = config
        self._expires_at = time.monotonic() + self._cache_ttl_seconds

    async def _fetch_config(self) -> PatentSourceConfig:
        try:
            doc = await self._container.read_item(
                item=_CONFIG_DOC_ID, partition_key=_CONFIG_PARTITION_KEY
            )
            return _parse_config_doc(doc)
        except CosmosResourceNotFoundError:
            logger.info("patent-sources config doc not found; defaulting to all sources enabled")
            return _ALL_SOURCES_ENABLED
        except Exception as exc:
            logger.error(
                "patent-sources config fetch failed error=%s; defaulting to all sources enabled",
                exc,
            )
            return _ALL_SOURCES_ENABLED
