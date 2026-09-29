import pytest

from evidence.application.handlers.load_corpus_handler import LoadCorpusDeps, LoadCorpusHandler
from evidence.domain.errors.evidence_errors import CorpusLoadError
from evidence.domain.models.patent_corpus_record import PatentCorpusRecord
from evidence.infrastructure.config.settings import EvidenceSettings


def _make_record(reference: str = "US1000") -> PatentCorpusRecord:
    return PatentCorpusRecord(
        reference=reference,
        title="Patent A",
        abstract="An abstract.",
        applicant="Acme Corp",
        date="2024-01-01",
        url="https://patents.example.com/US1000",
        metadata={},
    )


class FakeLoader:
    def __init__(self, loaded: int | None = None, error: Exception | None = None) -> None:
        self._loaded = loaded
        self._error = error
        self.calls: list[list[PatentCorpusRecord]] = []

    async def embed_and_upsert(self, records: list[PatentCorpusRecord]) -> int:
        self.calls.append(records)
        if self._error:
            raise self._error
        return self._loaded if self._loaded is not None else len(records)


class FakeReader:
    def __init__(self, records: list[PatentCorpusRecord]) -> None:
        self._records = records
        self.read_calls: list[str | None] = []

    def read(self, file_path: str | None = None) -> list[PatentCorpusRecord]:
        self.read_calls.append(file_path)
        return self._records


def _make_handler(loader=None, reader=None) -> tuple[LoadCorpusHandler, FakeLoader, FakeReader]:
    fake_loader = loader or FakeLoader()
    fake_reader = reader or FakeReader([_make_record()])
    settings = EvidenceSettings(
        model_router_url="http://test-model-router",
        vector_router_url="http://test-vector-router",
    )
    deps = LoadCorpusDeps(loader=fake_loader, reader=fake_reader, settings=settings)
    return LoadCorpusHandler(deps), fake_loader, fake_reader


@pytest.mark.asyncio
async def test_bulk_load_reads_then_loads():
    records = [_make_record("US1000"), _make_record("US1001")]
    handler, loader, reader = _make_handler(reader=FakeReader(records))

    response = await handler.bulk_load()

    assert reader.read_calls == [None]
    assert loader.calls == [records]
    assert response.loaded_count == 2
    assert response.skipped_count == 0
    assert response.failed_count == 0


@pytest.mark.asyncio
async def test_bulk_load_passes_file_path_override():
    handler, _, reader = _make_handler()

    await handler.bulk_load(file_path="custom.jsonl")

    assert reader.read_calls == ["custom.jsonl"]


@pytest.mark.asyncio
async def test_incremental_add_skips_read():
    records = [_make_record("US2000")]
    handler, loader, reader = _make_handler()

    response = await handler.incremental_add(records)

    assert reader.read_calls == []
    assert loader.calls == [records]
    assert response.loaded_count == 1


@pytest.mark.asyncio
async def test_incremental_add_with_empty_records_returns_zero_counts():
    handler, loader, _ = _make_handler()

    response = await handler.incremental_add([])

    assert loader.calls == []
    assert response.loaded_count == 0
    assert response.skipped_count == 0
    assert response.failed_count == 0


@pytest.mark.asyncio
async def test_load_records_reports_skipped_when_loader_loads_fewer():
    records = [_make_record("US1000"), _make_record("US1001")]
    handler, _, _ = _make_handler(loader=FakeLoader(loaded=1))

    response = await handler.incremental_add(records)

    assert response.loaded_count == 1
    assert response.skipped_count == 1
    assert response.failed_count == 0


@pytest.mark.asyncio
async def test_load_records_reports_failed_on_corpus_load_error():
    records = [_make_record("US1000"), _make_record("US1001")]
    handler, _, _ = _make_handler(loader=FakeLoader(error=CorpusLoadError("vector router down")))

    response = await handler.incremental_add(records)

    assert response.loaded_count == 0
    assert response.skipped_count == 0
    assert response.failed_count == 2
