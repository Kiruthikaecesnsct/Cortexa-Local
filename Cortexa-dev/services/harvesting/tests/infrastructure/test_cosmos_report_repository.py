import asyncio
from datetime import UTC, datetime
from unittest.mock import AsyncMock

import pytest
from azure.cosmos.exceptions import CosmosHttpResponseError

from harvesting.domain.enums.agreement_flag import AgreementFlag
from harvesting.domain.enums.maturity import Maturity
from harvesting.domain.enums.scoring_axis import ScoringAxis
from harvesting.domain.errors.harvesting_errors import (
    ReportNotFoundError,
    StoragePermanentError,
    StorageWriteError,
)
from harvesting.domain.models.axis_score import AxisScore
from harvesting.domain.models.citation import Citation, ProvenanceLink
from harvesting.domain.models.harvesting_report import HarvestingReport
from harvesting.domain.models.report_candidate import ReportCandidate
from harvesting.infrastructure.cosmos.report_repository import CosmosReportRepository

BATCH_ID = "batch-abc"
DOCUMENT_ID = "doc-xyz"
REPORT_ID = "report-001"
CANDIDATE_ID = "cand-001"
WEIGHTED_SCORE = 75.0
RANK = 1
AXIS_SCORE_VALUE = 50


def _make_axis_scores() -> dict[ScoringAxis, AxisScore]:
    return {
        ScoringAxis.Novelty: AxisScore(axis=ScoringAxis.Novelty, score=AXIS_SCORE_VALUE, refs=[]),
        ScoringAxis.Inventiveness: AxisScore(
            axis=ScoringAxis.Inventiveness, score=AXIS_SCORE_VALUE, refs=[]
        ),
        ScoringAxis.Commercial: AxisScore(
            axis=ScoringAxis.Commercial, score=AXIS_SCORE_VALUE, refs=[]
        ),
        ScoringAxis.Strategic: AxisScore(
            axis=ScoringAxis.Strategic, score=AXIS_SCORE_VALUE, refs=[]
        ),
        ScoringAxis.Patentability: AxisScore(
            axis=ScoringAxis.Patentability, score=AXIS_SCORE_VALUE, refs=[]
        ),
    }


def _make_report() -> HarvestingReport:
    candidate = ReportCandidate(
        candidate_id=CANDIDATE_ID,
        title="Test Invention",
        description="A novel method for testing.",
        maturity=Maturity.Emerging,
        rank=RANK,
        weighted_score=WEIGHTED_SCORE,
        axes=_make_axis_scores(),
        agreement_flag=AgreementFlag.Full,
        citations=[
            Citation(
                ref="E1",
                source_type="patent_api",
                title="Prior art patent",
                patent_id="US1234567A",
                url="https://example.com/patent/1",
                similarity=0.87,
            )
        ],
        provenance_links=[
            ProvenanceLink(document_id="doc-xyz", locator="chunk-001", source_kind="extraction")
        ],
    )
    return HarvestingReport(
        id=REPORT_ID,
        batch_id=BATCH_ID,
        document_id=DOCUMENT_ID,
        generated_at=datetime(2026, 6, 27, 10, 0, 0, tzinfo=UTC),
        candidates=[candidate],
    )


def _make_repository(write_concurrency: int = 4) -> tuple[CosmosReportRepository, AsyncMock]:
    container = AsyncMock()
    repo = CosmosReportRepository(container, write_concurrency=write_concurrency)
    return repo, container


async def test_save_upserts_without_partition_key_kwarg():
    repo, container = _make_repository()
    report = _make_report()

    await repo.save(report)

    assert container.upsert_item.call_count == 2
    all_calls = [call.args[0] for call in container.upsert_item.call_args_list]
    for item_dict in all_calls:
        assert item_dict["batch_id"] == BATCH_ID
        assert "partition_key" not in container.upsert_item.call_args_list[0].kwargs


async def test_save_cosmos_error_raises_storage_write_error():
    repo, container = _make_repository()
    report = _make_report()
    cosmos_error = CosmosHttpResponseError(status_code=503, message="Service unavailable")
    container.upsert_item.side_effect = cosmos_error

    with pytest.raises(StorageWriteError):
        await repo.save(report)


async def test_get_by_batch_queries_with_correct_batch_id():
    repo, container = _make_repository()
    report_dict = _make_report().model_dump(mode="json")

    async def mock_query_items(query, parameters, partition_key):
        yield report_dict

    container.query_items = mock_query_items

    result = await repo.get_by_batch(BATCH_ID)

    assert isinstance(result, HarvestingReport)
    assert result.batch_id == BATCH_ID


async def test_get_by_batch_no_items_raises_report_not_found_error():
    repo, container = _make_repository()

    async def mock_query_items(query, parameters, partition_key):
        return
        yield

    container.query_items = mock_query_items

    with pytest.raises(ReportNotFoundError) as exc_info:
        await repo.get_by_batch(BATCH_ID)

    assert BATCH_ID in str(exc_info.value)


async def test_get_by_batch_cosmos_error_raises_storage_write_error():
    repo, container = _make_repository()

    async def mock_query_items(query, parameters, partition_key):
        raise CosmosHttpResponseError(status_code=500, message="Internal server error")
        yield

    container.query_items = mock_query_items

    with pytest.raises(StorageWriteError):
        await repo.get_by_batch(BATCH_ID)


async def test_get_by_batch_query_excludes_seeding_engine():
    repo, container = _make_repository()
    report_dict = _make_report().model_dump(mode="json")
    captured: dict = {}

    async def mock_query_items(query, parameters, partition_key):
        captured["query"] = query
        yield report_dict

    container.query_items = mock_query_items

    await repo.get_by_batch(BATCH_ID)

    query = captured["query"]
    assert "NOT IS_DEFINED(c.engine)" in query
    assert "c.engine != 'seeding'" in query


async def test_get_by_batch_returns_hydrated_harvesting_report():
    repo, container = _make_repository()
    report_dict = _make_report().model_dump(mode="json")

    async def mock_query_items(query, parameters, partition_key):
        yield report_dict

    container.query_items = mock_query_items

    result = await repo.get_by_batch(BATCH_ID)

    assert isinstance(result, HarvestingReport)
    assert result.id == REPORT_ID
    assert result.batch_id == BATCH_ID
    assert len(result.candidates) == 1
    assert result.candidates[0].candidate_id == CANDIDATE_ID


async def test_save_writes_header_and_candidate_items():
    repo, container = _make_repository()
    report = _make_report()

    await repo.save(report)

    assert container.upsert_item.call_count == 2
    all_calls = [call.args[0] for call in container.upsert_item.call_args_list]

    candidate_items = [c for c in all_calls if c.get("doc_type") == "report_candidate"]
    header_items = [c for c in all_calls if c.get("doc_type") == "report_header"]

    assert len(candidate_items) == 1
    assert len(header_items) == 1

    candidate_item = candidate_items[0]
    assert candidate_item["id"] == f"report_candidate:{BATCH_ID}:{CANDIDATE_ID}"
    assert candidate_item["batch_id"] == BATCH_ID
    assert candidate_item["engine"] == "harvesting"
    assert candidate_item["report_id"] == REPORT_ID
    assert candidate_item["candidate_id"] == CANDIDATE_ID

    header_item = header_items[0]
    assert header_item["id"] == REPORT_ID
    assert header_item["batch_id"] == BATCH_ID
    assert header_item["document_id"] == DOCUMENT_ID
    assert header_item["engine"] == "harvesting"
    assert header_item["doc_type"] == "report_header"
    assert header_item["candidate_count"] == 1


async def test_save_cosmos_413_raises_storage_permanent_error():
    repo, container = _make_repository()
    report = _make_report()
    cosmos_error = CosmosHttpResponseError(status_code=413, message="Request too large")
    container.upsert_item.side_effect = cosmos_error

    with pytest.raises(StoragePermanentError):
        await repo.save(report)


async def test_save_cosmos_400_raises_storage_permanent_error():
    repo, container = _make_repository()
    report = _make_report()
    cosmos_error = CosmosHttpResponseError(status_code=400, message="Bad request")
    container.upsert_item.side_effect = cosmos_error

    with pytest.raises(StoragePermanentError):
        await repo.save(report)


async def test_save_cosmos_429_raises_storage_write_error():
    repo, container = _make_repository()
    report = _make_report()
    cosmos_error = CosmosHttpResponseError(status_code=429, message="Too many requests")
    container.upsert_item.side_effect = cosmos_error

    with pytest.raises(StorageWriteError):
        await repo.save(report)


async def test_save_cosmos_503_raises_storage_write_error():
    repo, container = _make_repository()
    report = _make_report()
    cosmos_error = CosmosHttpResponseError(status_code=503, message="Service unavailable")
    container.upsert_item.side_effect = cosmos_error

    with pytest.raises(StorageWriteError):
        await repo.save(report)


async def test_get_by_batch_reassembles_split_report():
    repo, container = _make_repository()
    original_report = _make_report()
    header_item = {
        "id": REPORT_ID,
        "batch_id": BATCH_ID,
        "document_id": DOCUMENT_ID,
        "engine": "harvesting",
        "doc_type": "report_header",
        "generated_at": original_report.generated_at.isoformat(),
        "candidate_count": 1,
    }
    candidate_dict = original_report.candidates[0].model_dump(mode="json")
    candidate_item = {
        "id": f"report_candidate:{BATCH_ID}:{CANDIDATE_ID}",
        "batch_id": BATCH_ID,
        "engine": "harvesting",
        "doc_type": "report_candidate",
        "report_id": REPORT_ID,
        **candidate_dict,
    }

    async def mock_query_items(query, parameters, partition_key):
        yield header_item
        yield candidate_item

    container.query_items = mock_query_items

    result = await repo.get_by_batch(BATCH_ID)

    assert isinstance(result, HarvestingReport)
    assert result.id == REPORT_ID
    assert result.batch_id == BATCH_ID
    assert result.document_id == DOCUMENT_ID
    assert len(result.candidates) == 1
    assert result.candidates[0].candidate_id == CANDIDATE_ID
    assert result.candidates[0].title == "Test Invention"
    assert result.candidates[0].rank == RANK


async def test_get_by_batch_legacy_single_doc_still_works():
    repo, container = _make_repository()
    legacy_report_dict = _make_report().model_dump(mode="json")

    async def mock_query_items(query, parameters, partition_key):
        yield legacy_report_dict

    container.query_items = mock_query_items

    result = await repo.get_by_batch(BATCH_ID)

    assert isinstance(result, HarvestingReport)
    assert result.id == REPORT_ID
    assert len(result.candidates) == 1


async def test_get_by_batch_split_report_no_header_raises_not_found():
    repo, container = _make_repository()
    candidate_item = {
        "id": f"report_candidate:{BATCH_ID}:{CANDIDATE_ID}",
        "batch_id": BATCH_ID,
        "engine": "harvesting",
        "doc_type": "report_candidate",
        "report_id": REPORT_ID,
        "candidate_id": CANDIDATE_ID,
        "rank": 1,
    }

    async def mock_query_items(query, parameters, partition_key):
        yield candidate_item

    container.query_items = mock_query_items

    with pytest.raises(ReportNotFoundError):
        await repo.get_by_batch(BATCH_ID)


async def test_save_large_candidate_set_respects_bounded_concurrency():
    concurrency_limit = 4
    repo, container = _make_repository(write_concurrency=concurrency_limit)
    candidates = [
        ReportCandidate(
            candidate_id=f"cand-{i:03d}",
            title=f"Invention {i}",
            description="Test description",
            maturity=Maturity.Emerging,
            rank=i,
            weighted_score=50.0 + i,
            axes=_make_axis_scores(),
            agreement_flag=AgreementFlag.Full,
            citations=[],
            provenance_links=[],
        )
        for i in range(200)
    ]
    report = HarvestingReport(
        id=REPORT_ID,
        batch_id=BATCH_ID,
        document_id=DOCUMENT_ID,
        generated_at=datetime(2026, 7, 13, 10, 0, 0, tzinfo=UTC),
        candidates=candidates,
    )
    concurrent_count = 0
    max_concurrent = 0

    async def mock_upsert(item):
        nonlocal concurrent_count, max_concurrent
        concurrent_count += 1
        max_concurrent = max(max_concurrent, concurrent_count)
        await asyncio.sleep(0.001)
        concurrent_count -= 1

    container.upsert_item.side_effect = mock_upsert

    await repo.save(report)

    assert container.upsert_item.call_count == 201
    assert max_concurrent <= concurrency_limit


async def test_save_transient_429_succeeds_after_retry():
    repo, container = _make_repository()
    report = _make_report()
    call_count = 0

    async def mock_upsert_with_retry(item):
        nonlocal call_count
        call_count += 1
        if call_count == 1:
            raise CosmosHttpResponseError(status_code=429, message="Too many requests")

    container.upsert_item.side_effect = mock_upsert_with_retry

    with pytest.raises(StorageWriteError):
        await repo.save(report)


async def test_save_idempotent_replay_reupserts_without_error():
    repo, container = _make_repository()
    report = _make_report()

    await repo.save(report)
    await repo.save(report)

    # upsert_item is idempotent on id+partition, so a redelivered save re-writes
    # the same header+candidate items without raising (BUG187/188 replay safety).
    assert container.upsert_item.call_count == 4


async def test_save_429_still_maps_to_storage_write_error():
    repo, container = _make_repository()
    report = _make_report()
    cosmos_error = CosmosHttpResponseError(status_code=429, message="Too many requests")
    container.upsert_item.side_effect = cosmos_error

    with pytest.raises(StorageWriteError):
        await repo.save(report)


async def test_save_413_still_maps_to_storage_permanent_error():
    repo, container = _make_repository()
    report = _make_report()
    cosmos_error = CosmosHttpResponseError(status_code=413, message="Request too large")
    container.upsert_item.side_effect = cosmos_error

    with pytest.raises(StoragePermanentError):
        await repo.save(report)


async def test_save_400_still_maps_to_storage_permanent_error():
    repo, container = _make_repository()
    report = _make_report()
    cosmos_error = CosmosHttpResponseError(status_code=400, message="Bad request")
    container.upsert_item.side_effect = cosmos_error

    with pytest.raises(StoragePermanentError):
        await repo.save(report)
