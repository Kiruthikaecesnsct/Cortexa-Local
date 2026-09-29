from extraction.infrastructure.cosmos.chunk_reader import ChunkReader

BATCH_ID = "batch-001"
DOCUMENT_ID = "doc-001"
MAX_EXCERPT_CHARS = 400


def _make_item(order_index: int) -> dict:
    return {
        "start_char": order_index * 100,
        "end_char": order_index * 100 + 50,
        "text": f"chunk text {order_index}",
        "order_index": order_index,
        "section_hint": None,
        "page_number": None,
    }


class FakeContainerProxy:
    def __init__(self, items: list[dict]) -> None:
        self._items = items
        self.captured_calls: list[dict] = []

    def query_items(self, query: str, parameters: list[dict], partition_key: str):
        self.captured_calls.append(
            {"query": query, "parameters": parameters, "partition_key": partition_key}
        )
        return self._async_iter()

    async def _async_iter(self):
        for item in self._items:
            yield item


def _param_value(parameters: list[dict], name: str):
    for param in parameters:
        if param["name"] == name:
            return param["value"]
    raise KeyError(name)


async def test_get_chunks_without_range_reads_all_chunks_in_order():
    items = [_make_item(i) for i in range(5)]
    container = FakeContainerProxy(items)
    reader = ChunkReader(container, MAX_EXCERPT_CHARS)

    chunks = await reader.get_chunks(BATCH_ID, DOCUMENT_ID, "code")

    assert [c.order_index for c in chunks] == [0, 1, 2, 3, 4]
    call = container.captured_calls[0]
    assert "@start" not in [p["name"] for p in call["parameters"]]
    assert "@end" not in [p["name"] for p in call["parameters"]]
    assert "ORDER BY c.order_index" in call["query"]


async def test_get_chunks_without_range_uses_batch_id_as_partition_key():
    container = FakeContainerProxy([_make_item(0)])
    reader = ChunkReader(container, MAX_EXCERPT_CHARS)

    await reader.get_chunks(BATCH_ID, DOCUMENT_ID, "code")

    assert container.captured_calls[0]["partition_key"] == BATCH_ID


async def test_get_chunks_with_range_filters_by_order_index_bounds():
    items = [_make_item(i) for i in range(25, 50)]
    container = FakeContainerProxy(items)
    reader = ChunkReader(container, MAX_EXCERPT_CHARS)

    chunks = await reader.get_chunks(BATCH_ID, DOCUMENT_ID, "code", order_index_range=(25, 50))

    assert len(chunks) == 25
    call = container.captured_calls[0]
    assert _param_value(call["parameters"], "@start") == 25
    assert _param_value(call["parameters"], "@end") == 50
    assert "c.order_index >= @start" in call["query"]
    assert "c.order_index < @end" in call["query"]


async def test_get_chunks_with_range_is_half_open_excludes_end():
    items = [_make_item(i) for i in range(0, 3)]
    container = FakeContainerProxy(items)
    reader = ChunkReader(container, MAX_EXCERPT_CHARS)

    await reader.get_chunks(BATCH_ID, DOCUMENT_ID, "code", order_index_range=(0, 25))

    call = container.captured_calls[0]
    assert _param_value(call["parameters"], "@end") == 25


async def test_get_chunks_range_none_is_equivalent_to_full_document_query():
    container = FakeContainerProxy([_make_item(0)])
    reader = ChunkReader(container, MAX_EXCERPT_CHARS)

    await reader.get_chunks(BATCH_ID, DOCUMENT_ID, "code", order_index_range=None)

    call = container.captured_calls[0]
    param_names = {p["name"] for p in call["parameters"]}
    assert param_names == {"@doc_id"}


async def test_get_chunks_builds_provenance_span_with_source_kind():
    container = FakeContainerProxy([_make_item(0)])
    reader = ChunkReader(container, MAX_EXCERPT_CHARS)

    chunks = await reader.get_chunks(BATCH_ID, DOCUMENT_ID, "code")

    assert chunks[0].source_span.source_kind == "code"
    assert chunks[0].source_span.locator == "chars:0-50"


async def test_get_chunks_truncates_excerpt_beyond_max_chars():
    item = _make_item(0)
    item["start_char"] = 0
    item["end_char"] = 1000
    item["text"] = "x" * 1000
    container = FakeContainerProxy([item])
    reader = ChunkReader(container, max_excerpt_chars=10)

    chunks = await reader.get_chunks(BATCH_ID, DOCUMENT_ID, "code")

    assert chunks[0].source_span.excerpt == "x" * 10
