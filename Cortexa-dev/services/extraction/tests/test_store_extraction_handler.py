from datetime import UTC, datetime

import pytest
from pydantic import ValidationError

from extraction.application.dtos.store_extraction_request import StoreExtractionRequest
from extraction.application.dtos.store_extraction_response import StoreExtractionResponse
from extraction.application.handlers.store_extraction_handler import (
    StoreExtractionDeps,
    StoreExtractionHandler,
)
from extraction.domain.errors.storage_errors import (
    PartialSaveError,
    RollbackError,
    StorageWriteError,
)
from extraction.domain.events.event_envelope import EventEnvelope
from extraction.domain.models.invention_candidate import InventionCandidate
from extraction.domain.value_objects.provenance_span import ProvenanceSpan


def _make_candidate(
    candidate_id: str, document_id: str = "doc-1", batch_id: str = "batch-1"
) -> InventionCandidate:
    return InventionCandidate(
        id=candidate_id,
        document_id=document_id,
        batch_id=batch_id,
        claim_text="A method for doing X",
        problem="Problem Y",
        mechanism="Mechanism Z",
        tech_field="Computer Science",
        ipc_cpc_guess=None,
        source_span=ProvenanceSpan(source_kind="paper", locator="section 2.1"),
        source_chunk_index=0,
        created_at=datetime(2024, 1, 1, tzinfo=UTC),
    )


def _make_request(
    candidates: list[InventionCandidate] | None = None,
    document_id: str = "doc-1",
    job_id: str = "batch-1",
    trigger_type: str = "harvest",
    correlation_id: str | None = "corr-abc",
) -> StoreExtractionRequest:
    if candidates is None:
        candidates = [_make_candidate("cand-1", document_id, job_id)]
    return StoreExtractionRequest(
        candidates=candidates,
        job_id=job_id,
        document_id=document_id,
        trigger_type=trigger_type,
        correlation_id=correlation_id,
    )


class FakeCandidateRepository:
    def __init__(self) -> None:
        self.saved: list[InventionCandidate] = []
        self.deleted: list[tuple[str, str]] = []
        self.fail_after: int | None = None  # raise PartialSaveError after N successful saves
        self.delete_error: Exception | None = None

    async def save_many(self, candidates: list[InventionCandidate]) -> list[str]:
        saved_ids: list[str] = []
        for i, candidate in enumerate(candidates):
            if self.fail_after is not None and i >= self.fail_after:
                raise PartialSaveError("simulated mid-loop failure", saved_ids)
            self.saved.append(candidate)
            saved_ids.append(candidate.id)
        return saved_ids

    async def delete_many(self, batch_id: str, candidate_ids: list[str]) -> None:
        if self.delete_error:
            raise self.delete_error
        for cid in candidate_ids:
            self.deleted.append((batch_id, cid))


class FakeEventPublisher:
    def __init__(self) -> None:
        self.published: list[tuple[str, EventEnvelope]] = []
        self.publish_error: Exception | None = None

    async def publish(self, topic: str, event: EventEnvelope) -> None:
        if self.publish_error:
            raise self.publish_error
        self.published.append((topic, event))


def _make_handler(
    repo: FakeCandidateRepository | None = None,
    publisher: FakeEventPublisher | None = None,
    topic: str = "extraction.completed",
) -> tuple[StoreExtractionHandler, FakeCandidateRepository, FakeEventPublisher]:
    repo = repo or FakeCandidateRepository()
    publisher = publisher or FakeEventPublisher()
    deps = StoreExtractionDeps(
        candidate_repo=repo,
        event_publisher=publisher,
        extraction_completed_topic=topic,
    )
    return StoreExtractionHandler(deps=deps), repo, publisher


async def test_happy_path_saves_and_publishes():
    candidates = [
        _make_candidate("cand-1"),
        _make_candidate("cand-2"),
    ]
    request = _make_request(candidates=candidates, document_id="doc-42", job_id="batch-99")
    handler, repo, publisher = _make_handler()

    result = await handler.handle(request)

    assert isinstance(result, StoreExtractionResponse)
    assert result.document_id == "doc-42"
    assert result.candidate_count == 2
    assert set(result.candidate_ids) == {"cand-1", "cand-2"}

    assert len(repo.saved) == 2
    assert {c.id for c in repo.saved} == {"cand-1", "cand-2"}

    assert len(publisher.published) == 1
    topic, event = publisher.published[0]
    assert topic == "extraction.completed"
    assert event.batch_id == "batch-99"
    assert event.document_id == "doc-42"
    assert event.payload["document_id"] == "doc-42"
    assert event.payload["job_id"] == "batch-99"
    assert event.payload["candidate_count"] == 2
    assert event.payload["trigger_type"] == "harvest"
    assert set(event.payload["candidate_ids"]) == {"cand-1", "cand-2"}


async def test_idempotent_upsert_handle_twice():
    candidates = [_make_candidate("cand-1")]
    request = _make_request(candidates=candidates)
    handler, repo, publisher = _make_handler()

    await handler.handle(request)
    await handler.handle(request)

    assert len(repo.saved) == 2
    assert len(publisher.published) == 2


async def test_rollback_on_publish_failure():
    candidates = [_make_candidate("cand-1"), _make_candidate("cand-2")]
    request = _make_request(candidates=candidates, document_id="doc-rollback", job_id="batch-99")
    repo = FakeCandidateRepository()
    publisher = FakeEventPublisher()
    publisher.publish_error = RuntimeError("service bus unavailable")
    handler, _, _ = _make_handler(repo=repo, publisher=publisher)

    with pytest.raises(StorageWriteError):
        await handler.handle(request)

    deleted_ids = [cid for _, cid in repo.deleted]
    assert set(deleted_ids) == {"cand-1", "cand-2"}
    assert all(batch_id == "batch-99" for batch_id, _ in repo.deleted)


async def test_rollback_failure_raises_rollback_error():
    candidates = [_make_candidate("cand-1")]
    request = _make_request(candidates=candidates)
    repo = FakeCandidateRepository()
    repo.delete_error = RuntimeError("cosmos unreachable")
    publisher = FakeEventPublisher()
    publisher.publish_error = RuntimeError("service bus unavailable")
    handler, _, _ = _make_handler(repo=repo, publisher=publisher)

    with pytest.raises(RollbackError):
        await handler.handle(request)


async def test_partial_save_triggers_rollback_of_written_ids():
    candidates = [_make_candidate("cand-1"), _make_candidate("cand-2"), _make_candidate("cand-3")]
    request = _make_request(candidates=candidates, document_id="doc-partial", job_id="batch-1")
    repo = FakeCandidateRepository()
    repo.fail_after = 2  # first 2 succeed, 3rd raises PartialSaveError
    handler, _, publisher = _make_handler(repo=repo)

    with pytest.raises(StorageWriteError):
        await handler.handle(request)

    deleted_ids = [cid for _, cid in repo.deleted]
    assert set(deleted_ids) == {"cand-1", "cand-2"}
    assert all(batch_id == "batch-1" for batch_id, _ in repo.deleted)
    assert len(publisher.published) == 0


async def test_zero_candidates_publishes_no_candidates_flag():
    request = _make_request(candidates=[], document_id="doc-empty", job_id="batch-empty")
    handler, repo, publisher = _make_handler()

    result = await handler.handle(request)

    assert isinstance(result, StoreExtractionResponse)
    assert result.candidate_count == 0
    assert result.candidate_ids == []
    assert len(repo.saved) == 0

    assert len(publisher.published) == 1
    topic, event = publisher.published[0]
    assert topic == "extraction.completed"
    assert event.payload["candidate_count"] == 0
    assert event.payload["candidate_ids"] == []
    assert event.payload["no_candidates"] is True


async def test_nonzero_candidates_does_not_set_no_candidates_flag():
    candidates = [_make_candidate("cand-1")]
    request = _make_request(candidates=candidates)
    handler, repo, publisher = _make_handler()

    await handler.handle(request)

    topic, event = publisher.published[0]
    assert event.payload["no_candidates"] is False


def test_empty_trigger_type_raises_validation_error():
    with pytest.raises(ValidationError):
        StoreExtractionRequest(
            candidates=[],
            job_id="batch-1",
            document_id="doc-1",
            trigger_type="",
            correlation_id=None,
        )
