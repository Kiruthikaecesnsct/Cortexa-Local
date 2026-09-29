import logging
from dataclasses import dataclass

from evidence.application.dtos.load_corpus_response import LoadCorpusResponseDto
from evidence.domain.errors.evidence_errors import CorpusLoadError
from evidence.domain.models.patent_corpus_record import PatentCorpusRecord
from evidence.domain.repositories.storage_protocols import CorpusLoader, CorpusReader
from evidence.infrastructure.config.settings import EvidenceSettings

_logger = logging.getLogger(__name__)


@dataclass
class LoadCorpusDeps:
    loader: CorpusLoader
    reader: CorpusReader
    settings: EvidenceSettings


class LoadCorpusHandler:
    def __init__(self, deps: LoadCorpusDeps) -> None:
        self._deps = deps

    async def bulk_load(self, file_path: str | None = None) -> LoadCorpusResponseDto:
        records = self._deps.reader.read(file_path)
        return await self._load_records(records)

    async def incremental_add(self, records: list[PatentCorpusRecord]) -> LoadCorpusResponseDto:
        return await self._load_records(records)

    async def _load_records(self, records: list[PatentCorpusRecord]) -> LoadCorpusResponseDto:
        if not records:
            return LoadCorpusResponseDto(loaded_count=0, skipped_count=0, failed_count=0)
        try:
            loaded = await self._deps.loader.embed_and_upsert(records)
        except CorpusLoadError as exc:
            _logger.error("Corpus load failed: %s", exc)
            return LoadCorpusResponseDto(
                loaded_count=exc.loaded_count,
                skipped_count=0,
                failed_count=len(records) - exc.loaded_count,
            )
        skipped = max(0, len(records) - loaded)
        return LoadCorpusResponseDto(loaded_count=loaded, skipped_count=skipped, failed_count=0)
