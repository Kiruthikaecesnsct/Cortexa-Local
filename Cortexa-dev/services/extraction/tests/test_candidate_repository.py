import json
from datetime import UTC, datetime

from extraction.domain.models.invention_candidate import InventionCandidate
from extraction.domain.value_objects.provenance_span import ProvenanceSpan
from extraction.infrastructure.cosmos.candidate_repository import CandidateRepository


class FakeContainerProxy:
    def __init__(self) -> None:
        self.upserted_items: list[dict] = []
        self.deleted_calls: list[tuple[str, str]] = []
        self.upsert_error: Exception | None = None
        self.delete_error: Exception | None = None
        self.count_result: int = 0
        self.last_query: str | None = None
        self.last_parameters: list[dict] | None = None
        self.last_partition_key: str | None = None

    async def upsert_item(self, item: dict) -> None:
        if self.upsert_error:
            raise self.upsert_error
        self.upserted_items.append(item)

    async def delete_item(self, item: str, partition_key: str) -> None:
        if self.delete_error:
            raise self.delete_error
        self.deleted_calls.append((item, partition_key))

    def query_items(self, query: str, parameters: list[dict], partition_key: str):
        self.last_query = query
        self.last_parameters = parameters
        self.last_partition_key = partition_key
        return self._count_iter()

    async def _count_iter(self):
        yield self.count_result


def _make_candidate(
    candidate_id: str = "cand-test-1",
    document_id: str = "doc-1",
    batch_id: str = "batch-1",
    created_at: datetime | None = None,
) -> InventionCandidate:
    if created_at is None:
        created_at = datetime(2024, 6, 15, 10, 30, 0, tzinfo=UTC)
    return InventionCandidate(
        id=candidate_id,
        document_id=document_id,
        batch_id=batch_id,
        claim_text="A method for distributed consensus using quantum entanglement",
        problem="Traditional consensus algorithms face latency and security challenges",
        mechanism="Leverages quantum entanglement for instantaneous state synchronization",
        tech_field="Quantum Computing",
        ipc_cpc_guess="G06F 9/46",
        source_span=ProvenanceSpan(source_kind="paper", locator="section 3.2"),
        source_chunk_index=5,
        created_at=created_at,
    )


async def test_save_many_produces_json_serializable_items():
    fake_container = FakeContainerProxy()
    repo = CandidateRepository(container=fake_container)

    candidates = [_make_candidate(candidate_id="cand-1"), _make_candidate(candidate_id="cand-2")]
    saved_ids = await repo.save_many(candidates)

    assert saved_ids == ["cand-1", "cand-2"]
    assert len(fake_container.upserted_items) == 2

    for item in fake_container.upserted_items:
        json_str = json.dumps(item)
        assert json_str is not None


async def test_save_many_converts_datetime_to_iso8601_string():
    fake_container = FakeContainerProxy()
    repo = CandidateRepository(container=fake_container)

    created_at = datetime(2024, 6, 15, 10, 30, 45, tzinfo=UTC)
    candidate = _make_candidate(candidate_id="cand-dt", created_at=created_at)
    await repo.save_many([candidate])

    item = fake_container.upserted_items[0]
    assert "created_at" in item
    assert isinstance(item["created_at"], str)
    assert "2024-06-15T10:30:45" in item["created_at"]


async def test_save_many_serializes_provenance_span_as_dict():
    fake_container = FakeContainerProxy()
    repo = CandidateRepository(container=fake_container)

    candidate = _make_candidate(candidate_id="cand-span")
    await repo.save_many([candidate])

    item = fake_container.upserted_items[0]
    assert "source_span" in item
    assert isinstance(item["source_span"], dict)
    assert item["source_span"]["source_kind"] == "paper"
    assert item["source_span"]["locator"] == "section 3.2"


async def test_save_many_includes_id_document_id_batch_id():
    fake_container = FakeContainerProxy()
    repo = CandidateRepository(container=fake_container)

    candidate = _make_candidate(
        candidate_id="cand-fields",
        document_id="doc-abc",
        batch_id="batch-xyz",
    )
    await repo.save_many([candidate])

    item = fake_container.upserted_items[0]
    assert item["id"] == "cand-fields"
    assert item["document_id"] == "doc-abc"
    assert item["batch_id"] == "batch-xyz"


async def test_delete_many_uses_batch_id_as_partition_key():
    fake_container = FakeContainerProxy()
    repo = CandidateRepository(container=fake_container)

    batch_id = "batch-partition-test"
    candidate_ids = ["cand-1", "cand-2", "cand-3"]
    await repo.delete_many(batch_id, candidate_ids)

    assert len(fake_container.deleted_calls) == 3
    for item_id, partition_key in fake_container.deleted_calls:
        assert partition_key == batch_id
    deleted_ids = [item_id for item_id, _ in fake_container.deleted_calls]
    assert set(deleted_ids) == {"cand-1", "cand-2", "cand-3"}


async def test_exists_for_unit_returns_true_when_count_positive():
    fake_container = FakeContainerProxy()
    fake_container.count_result = 3
    repo = CandidateRepository(container=fake_container)

    result = await repo.exists_for_unit("batch-1", "doc-1", 2)

    assert result is True


async def test_exists_for_unit_returns_false_when_count_zero():
    fake_container = FakeContainerProxy()
    fake_container.count_result = 0
    repo = CandidateRepository(container=fake_container)

    result = await repo.exists_for_unit("batch-1", "doc-1", 2)

    assert result is False


async def test_exists_for_unit_filters_by_unit_index_and_document_id():
    fake_container = FakeContainerProxy()
    fake_container.count_result = 1
    repo = CandidateRepository(container=fake_container)

    await repo.exists_for_unit("batch-1", "doc-42", 7)

    assert fake_container.last_partition_key == "batch-1"
    param_map = {p["name"]: p["value"] for p in fake_container.last_parameters}
    assert param_map["@doc_id"] == "doc-42"
    assert param_map["@unit_index"] == 7
    assert "c.extraction_unit_index = @unit_index" in fake_container.last_query


async def test_exists_for_unit_does_not_match_a_different_units_candidates():
    fake_container = FakeContainerProxy()
    fake_container.count_result = 0
    repo = CandidateRepository(container=fake_container)

    result = await repo.exists_for_unit("batch-1", "doc-1", 5)

    assert result is False
