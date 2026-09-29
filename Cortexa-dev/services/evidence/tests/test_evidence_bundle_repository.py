from datetime import UTC, datetime
from unittest.mock import AsyncMock, MagicMock

import pytest
from azure.core.exceptions import ServiceRequestError
from azure.cosmos.exceptions import CosmosHttpResponseError

from evidence.domain.enums.confidence_band import ConfidenceBand
from evidence.domain.enums.evidence_source import EvidenceSource
from evidence.domain.errors.evidence_errors import StorageWriteError
from evidence.domain.models.evidence_bundle import EvidenceBundle
from evidence.infrastructure.cosmos.evidence_bundle_repository import (
    CosmosEvidenceBundleRepository,
)

BATCH_ID = "batch-001"
DOCUMENT_ID = "doc-001"
CANDIDATE_ID = "cand-001"


def _make_bundle(candidate_id: str = CANDIDATE_ID) -> EvidenceBundle:
    return EvidenceBundle(
        id="bundle-001",
        batch_id=BATCH_ID,
        job_id=BATCH_ID,
        candidate_id=candidate_id,
        document_id=DOCUMENT_ID,
        hits=[],
        confidence_band=ConfidenceBand.High,
        sources_used=[EvidenceSource.PatentApi],
        source_flags={
            EvidenceSource.PatentApi: True,
            EvidenceSource.SeedCorpus: False,
            EvidenceSource.LlmResearch: False,
        },
        merged_at=datetime.now(UTC),
        patent_source_results=[],
        degraded=False,
        degraded_sources=[],
        active_source_count=1,
        meets_minimum_sources=True,
        minimum_active_sources=1,
    )


@pytest.mark.asyncio
async def test_save_cosmos_http_response_error_wraps_as_storage_write_error():
    container = AsyncMock()
    container.upsert_item.side_effect = CosmosHttpResponseError(
        status_code=503, message="Service unavailable"
    )

    repository = CosmosEvidenceBundleRepository(container)
    bundle = _make_bundle()

    with pytest.raises(StorageWriteError) as exc_info:
        await repository.save(bundle)

    assert exc_info.value.__cause__.__class__ is CosmosHttpResponseError


@pytest.mark.asyncio
async def test_save_service_request_error_wraps_as_storage_write_error():
    container = AsyncMock()
    container.upsert_item.side_effect = ServiceRequestError("Connection reset by peer")

    repository = CosmosEvidenceBundleRepository(container)
    bundle = _make_bundle()

    with pytest.raises(StorageWriteError) as exc_info:
        await repository.save(bundle)

    assert exc_info.value.__cause__.__class__ is ServiceRequestError


@pytest.mark.asyncio
async def test_save_timeout_error_wraps_as_storage_write_error():
    container = AsyncMock()
    container.upsert_item.side_effect = TimeoutError()

    repository = CosmosEvidenceBundleRepository(container)
    bundle = _make_bundle()

    with pytest.raises(StorageWriteError) as exc_info:
        await repository.save(bundle)

    assert exc_info.value.__cause__.__class__ is TimeoutError


@pytest.mark.asyncio
async def test_find_for_candidate_cosmos_http_response_error_wraps_as_storage_write_error():
    container = MagicMock()

    class FailingAsyncIterator:
        def __aiter__(self):
            return self

        async def __anext__(self):
            raise CosmosHttpResponseError(status_code=500, message="Internal server error")

    container.query_items = MagicMock(return_value=FailingAsyncIterator())

    repository = CosmosEvidenceBundleRepository(container)

    with pytest.raises(StorageWriteError) as exc_info:
        await repository.find_for_candidate(BATCH_ID, DOCUMENT_ID, CANDIDATE_ID)

    assert exc_info.value.__cause__.__class__ is CosmosHttpResponseError


@pytest.mark.asyncio
async def test_find_for_candidate_service_request_error_wraps_as_storage_write_error():
    container = MagicMock()

    class FailingAsyncIterator:
        def __aiter__(self):
            return self

        async def __anext__(self):
            raise ServiceRequestError("Network unreachable")

    container.query_items = MagicMock(return_value=FailingAsyncIterator())

    repository = CosmosEvidenceBundleRepository(container)

    with pytest.raises(StorageWriteError) as exc_info:
        await repository.find_for_candidate(BATCH_ID, DOCUMENT_ID, CANDIDATE_ID)

    assert exc_info.value.__cause__.__class__ is ServiceRequestError


@pytest.mark.asyncio
async def test_find_for_candidate_timeout_error_wraps_as_storage_write_error():
    container = MagicMock()

    class FailingAsyncIterator:
        def __aiter__(self):
            return self

        async def __anext__(self):
            raise TimeoutError()

    container.query_items = MagicMock(return_value=FailingAsyncIterator())

    repository = CosmosEvidenceBundleRepository(container)

    with pytest.raises(StorageWriteError) as exc_info:
        await repository.find_for_candidate(BATCH_ID, DOCUMENT_ID, CANDIDATE_ID)

    assert exc_info.value.__cause__.__class__ is TimeoutError
