from seeding.infrastructure.cosmos.candidate_read_repository import CosmosCandidateReadRepository

BATCH_ID = "batch-filter"


class _FakeContainer:
    def __init__(self, items: list[dict]) -> None:
        self._items = items
        self.last_query: str | None = None
        self.last_parameters: list | None = None

    def query_items(self, query, parameters, partition_key):
        self.last_query = query
        self.last_parameters = parameters
        return _async_iter(self._items)


async def _async_iter(items):
    for item in items:
        yield item


async def test_get_by_batch_excludes_seeded_engine():
    container = _FakeContainer([])
    repo = CosmosCandidateReadRepository(container)

    await repo.get_by_batch(BATCH_ID)

    query = container.last_query
    assert "NOT IS_DEFINED(c.engine)" in query
    assert "c.engine != @engine" in query
    values = {p["name"]: p["value"] for p in container.last_parameters}
    assert values["@engine"] == "seeding"


async def test_get_seeded_by_batch_selects_only_seeded_engine():
    container = _FakeContainer([])
    repo = CosmosCandidateReadRepository(container)

    await repo.get_seeded_by_batch(BATCH_ID)

    query = container.last_query
    assert "c.engine = @engine" in query
    assert "!=" not in query
    values = {p["name"]: p["value"] for p in container.last_parameters}
    assert values["@engine"] == "seeding"


async def test_get_by_batch_returns_items():
    container = _FakeContainer([{"id": "cand-1", "batch_id": BATCH_ID}])
    repo = CosmosCandidateReadRepository(container)

    items = await repo.get_by_batch(BATCH_ID)

    assert items == [{"id": "cand-1", "batch_id": BATCH_ID}]
