from datetime import UTC, datetime

import pytest

from evidence.application.dtos.store_evidence_request import StoreEvidenceRequestDto
from evidence.application.handlers.store_evidence_handler import (
    StoreEvidenceDeps,
    StoreEvidenceHandler,
)
from evidence.domain.enums.confidence_band import ConfidenceBand
from evidence.domain.enums.evidence_source import EvidenceSource
from evidence.domain.errors.evidence_errors import EventPublishError, StorageWriteError
from evidence.domain.events.event_envelope import EventEnvelope
from evidence.domain.models.evidence_bundle import EvidenceBundle
from evidence.infrastructure.config.settings import EvidenceSettings

BUNDLE_ID = "bundle-001"
CANDIDATE_ID = "cand-001"
DOCUMENT_ID = "doc-001"
BATCH_ID = "batch-001"
JOB_ID = "job-001"


def _make_bundle(
    source_flags: dict[EvidenceSource, bool] | None = None,
) -> EvidenceBundle:
    flags = source_flags or {
        EvidenceSource.PatentApi: True,
        EvidenceSource.SeedCorpus: True,
        EvidenceSource.LlmResearch: True,
    }
    sources_used = [s for s, active in flags.items() if active]
    return EvidenceBundle(
        id=BUNDLE_ID,
        batch_id=BATCH_ID,
        job_id=JOB_ID,
        candidate_id=CANDIDATE_ID,
        document_id=DOCUMENT_ID,
        hits=[],
        confidence_band=ConfidenceBand.High,
        sources_used=sources_used,
        source_flags=flags,
        merged_at=datetime.now(UTC),
    )


class FakeRepository:
    def __init__(self) -> None:
        self.saved: list[EvidenceBundle] = []

    async def save(self, bundle: EvidenceBundle) -> None:
        self.saved.append(bundle)


class FailingRepository:
    async def save(self, bundle: EvidenceBundle) -> None:
        raise StorageWriteError("Cosmos unavailable")


class FakePublisher:
    def __init__(self) -> None:
        self.published: list[tuple[str, EventEnvelope]] = []

    async def publish(self, topic: str, event: EventEnvelope) -> None:
        self.published.append((topic, event))


class FailingPublisher:
    async def publish(self, topic: str, event: EventEnvelope) -> None:
        raise EventPublishError("Service Bus unavailable")


def _make_handler(repository=None, publisher=None) -> tuple[StoreEvidenceHandler, object, object]:
    repo = repository or FakeRepository()
    pub = publisher or FakePublisher()
    settings = EvidenceSettings(
        model_router_url="http://test-model-router",
        vector_router_url="http://test-vector-router",
    )
    deps = StoreEvidenceDeps(repository=repo, publisher=pub, settings=settings)
    return StoreEvidenceHandler(deps), repo, pub


@pytest.mark.asyncio
async def test_handle_all_sources_success():
    handler, repo, pub = _make_handler()
    bundle = _make_bundle()
    request = StoreEvidenceRequestDto(bundle=bundle)

    response = await handler.handle(request)

    assert len(repo.saved) == 1
    assert repo.saved[0].id == BUNDLE_ID
    assert len(pub.published) == 1
    assert response.bundle_id == BUNDLE_ID
    assert response.document_id == DOCUMENT_ID
    assert response.published is True


@pytest.mark.asyncio
async def test_handle_partial_sources():
    flags = {
        EvidenceSource.PatentApi: True,
        EvidenceSource.SeedCorpus: False,
        EvidenceSource.LlmResearch: False,
    }
    handler, repo, pub = _make_handler()
    bundle = _make_bundle(source_flags=flags)
    request = StoreEvidenceRequestDto(bundle=bundle)

    response = await handler.handle(request)

    assert len(repo.saved) == 1
    assert response.published is True

    _, event = pub.published[0]
    coverage = event.payload["source_coverage"]
    assert coverage["has_patent_api"] is True
    assert coverage["has_corpus"] is False
    assert coverage["has_llm"] is False


@pytest.mark.asyncio
async def test_handle_publish_failure_returns_published_false():
    handler, repo, _ = _make_handler(publisher=FailingPublisher())
    bundle = _make_bundle()
    request = StoreEvidenceRequestDto(bundle=bundle)

    response = await handler.handle(request)

    assert len(repo.saved) == 1
    assert response.published is False


@pytest.mark.asyncio
async def test_handle_storage_failure_raises():
    handler, _, _ = _make_handler(repository=FailingRepository())
    bundle = _make_bundle()
    request = StoreEvidenceRequestDto(bundle=bundle)

    with pytest.raises(StorageWriteError):
        await handler.handle(request)
