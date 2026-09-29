from azure.cosmos.aio import ContainerProxy
from azure.cosmos.exceptions import CosmosResourceNotFoundError

from extraction.domain.models.chunk_input import ChunkInput
from extraction.domain.value_objects.provenance_span import ProvenanceSpan

_CONTROL_CODEPOINTS = list(range(0x00, 0x20)) + [0x7F]
_BIDI_AND_ZERO_WIDTH_CODEPOINTS = (
    [0x2028, 0x2029]
    + list(range(0x200B, 0x2010))
    + list(range(0x202A, 0x202F))
    + list(range(0x2066, 0x206A))
)
_QUOTE_DELIMITER_CODEPOINT = 0x22
_UNSAFE_FILENAME_CHARS = "".join(
    chr(codepoint)
    for codepoint in [
        *_CONTROL_CODEPOINTS,
        *_BIDI_AND_ZERO_WIDTH_CODEPOINTS,
        _QUOTE_DELIMITER_CODEPOINT,
    ]
)
_UNSAFE_FILENAME_TRANSLATION = str.maketrans("", "", _UNSAFE_FILENAME_CHARS)
_MAX_FILENAME_LENGTH = 256


def _sanitize_filename(filename: str | None) -> str | None:
    if not filename:
        return None
    cleaned = filename.translate(_UNSAFE_FILENAME_TRANSLATION).strip()
    if not cleaned:
        return None
    return cleaned[:_MAX_FILENAME_LENGTH]


class DocumentSourceReader:
    def __init__(self, container: ContainerProxy) -> None:
        self._container = container

    async def get_source_meta(
        self, batch_id: str, document_id: str
    ) -> tuple[str, str | None] | None:
        try:
            item = await self._container.read_item(item=document_id, partition_key=batch_id)
        except CosmosResourceNotFoundError:
            return None
        source_kind = item.get("source_kind")
        if not source_kind:
            return None
        return source_kind, _sanitize_filename(item.get("filename"))


class ChunkReader:
    def __init__(self, container: ContainerProxy, max_excerpt_chars: int) -> None:
        self._container = container
        self._max_excerpt_chars = max_excerpt_chars

    async def get_chunks(
        self,
        batch_id: str,
        document_id: str,
        source_kind: str,
        order_index_range: tuple[int, int] | None = None,
    ) -> list[ChunkInput]:
        query, params = self._build_query(document_id, order_index_range)
        items = self._container.query_items(
            query=query,
            parameters=params,
            partition_key=batch_id,
        )
        return [self._to_chunk_input(item, document_id, source_kind) async for item in items]

    def _build_query(
        self, document_id: str, order_index_range: tuple[int, int] | None
    ) -> tuple[str, list[dict]]:
        params: list[dict] = [{"name": "@doc_id", "value": document_id}]
        if order_index_range is None:
            query = "SELECT * FROM c WHERE c.document_id = @doc_id ORDER BY c.order_index"
            return query, params
        start, end = order_index_range
        params.append({"name": "@start", "value": start})
        params.append({"name": "@end", "value": end})
        query = (
            "SELECT * FROM c WHERE c.document_id = @doc_id "
            "AND c.order_index >= @start AND c.order_index < @end "
            "ORDER BY c.order_index"
        )
        return query, params

    def _to_chunk_input(self, item: dict, document_id: str, source_kind: str) -> ChunkInput:
        start_char = item["start_char"]
        end_char = item["end_char"]
        chunk_text = item["text"]
        span_length = end_char - start_char
        excerpt = (
            chunk_text
            if span_length <= self._max_excerpt_chars
            else chunk_text[: self._max_excerpt_chars]
        )
        span = ProvenanceSpan(
            source_kind=source_kind,
            locator=f"chars:{start_char}-{end_char}",
            section_hint=item.get("section_hint"),
            span_start=start_char,
            span_end=end_char,
            page_number=item.get("page_number"),
            excerpt=excerpt,
        )
        return ChunkInput(
            text=chunk_text,
            order_index=item["order_index"],
            document_id=document_id,
            source_span=span,
        )
