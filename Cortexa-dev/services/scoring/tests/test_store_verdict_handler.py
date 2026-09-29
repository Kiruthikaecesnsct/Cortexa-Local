from unittest.mock import AsyncMock

import pytest
from azure.servicebus.exceptions import ServiceBusError

from scoring.application.dtos.store_verdict_request import StoreVerdictRequestDto
from scoring.application.handlers.store_verdict_handler import StoreVerdictDeps, StoreVerdictHandler
from scoring.domain.enums.agreement_level import AgreementLevel
from scoring.domain.enums.scoring_axis import ScoringAxis
from scoring.domain.errors.storage_errors import EventPublishError, StorageWriteError
from scoring.domain.models.axis_score import AxisScore
from scoring.domain.models.dual_scoring_verdict import DualScoringVerdict
from scoring.domain.models.scoring_verdict import ScoringVerdict
from scoring.infrastructure.config.settings import ScoringSettings

_AXES = {
    ScoringAxis.Novelty: AxisScore(axis=ScoringAxis.Novelty, score=80, refs=[]),
    ScoringAxis.Inventiveness: AxisScore(axis=ScoringAxis.Inventiveness, score=60, refs=[]),
    ScoringAxis.Commercial: AxisScore(axis=ScoringAxis.Commercial, score=70, refs=[]),
    ScoringAxis.Strategic: AxisScore(axis=ScoringAxis.Strategic, score=50, refs=[]),
    ScoringAxis.Patentability: AxisScore(axis=ScoringAxis.Patentability, score=90, refs=[]),
}

_PRIMARY = ScoringVerdict(
    batch_id="batch-1",
    job_id="job-1",
    candidate_id="cand-1",
    document_id="doc-1",
    axes=_AXES,
)

_DUAL = DualScoringVerdict(
    primary=_PRIMARY,
    secondary=None,
    agreement_level=AgreementLevel.FallbackSingle,
    agreeing_axis_count=0,
)


def _make_request(correlation_id: str | None = None) -> StoreVerdictRequestDto:
    return StoreVerdictRequestDto(dual_verdict=_DUAL, correlation_id=correlation_id)


def _make_deps(repo: AsyncMock, publisher: AsyncMock) -> StoreVerdictDeps:
    return StoreVerdictDeps(
        repository=repo,
        publisher=publisher,
        settings=ScoringSettings(
            cosmos_uri="https://test.documents.azure.com:443/",
            servicebus_namespace_fqdn="test.servicebus.windows.net",
            model_router_url="http://cortexa-dev-model-router.internal.example.azurecontainerapps.io",
        ),
    )


@pytest.mark.asyncio
async def test_happy_path_save_and_publish_called():
    repo = AsyncMock()
    publisher = AsyncMock()
    handler = StoreVerdictHandler(_make_deps(repo, publisher))

    response = await handler.handle(_make_request(correlation_id="corr-1"))

    repo.save.assert_awaited_once()
    publisher.publish.assert_awaited_once()
    assert response.published is True
    assert response.verdict_id == "batch-1:cand-1"
    assert response.document_id == "doc-1"


@pytest.mark.asyncio
async def test_publish_fails_raises_event_publish_error():
    repo = AsyncMock()
    publisher = AsyncMock()
    publisher.publish.side_effect = EventPublishError("bus down", verdict_id="v1", document_id="d1")
    handler = StoreVerdictHandler(_make_deps(repo, publisher))

    with pytest.raises(EventPublishError):
        await handler.handle(_make_request())

    repo.save.assert_awaited_once()


@pytest.mark.asyncio
async def test_storage_fails_raises_and_publish_not_called():
    repo = AsyncMock()
    publisher = AsyncMock()
    repo.save.side_effect = StorageWriteError("cosmos error")
    handler = StoreVerdictHandler(_make_deps(repo, publisher))

    with pytest.raises(StorageWriteError):
        await handler.handle(_make_request())

    publisher.publish.assert_not_awaited()


@pytest.mark.asyncio
async def test_raw_sdk_exception_wrapped_into_event_publish_error_with_verdict_context():
    repo = AsyncMock()
    publisher = AsyncMock()
    publisher.publish.side_effect = ServiceBusError("connection refused")
    handler = StoreVerdictHandler(_make_deps(repo, publisher))

    with pytest.raises(EventPublishError) as exc_info:
        await handler.handle(_make_request())

    error = exc_info.value
    assert error.verdict_id == "batch-1:cand-1"
    assert error.document_id == "doc-1"
    assert "connection refused" in str(error)
    repo.save.assert_awaited_once()
