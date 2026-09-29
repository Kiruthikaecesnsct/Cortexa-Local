from dataclasses import dataclass

from ingestion.application.provenance.chunk_id import build_chunk_id, build_code_chunk_id
from ingestion.application.provenance.line_mapper import char_range_to_line_range
from ingestion.domain.enums.source_kind import SourceKind
from ingestion.domain.models.chunk import Chunk
from ingestion.domain.models.provenance_entry import ProvenanceEntry
from ingestion.domain.models.provenance_map import ProvenanceMap


@dataclass(frozen=True)
class CodeFileContext:
    repo_id: str
    file_path: str
    text: str


def build_paper_entries(doc_id: str, text: str, chunks: list[Chunk]) -> list[ProvenanceEntry]:
    entries = []
    for chunk in chunks:
        chunk_id = build_chunk_id(doc_id, chunk.order_index)
        byte_start = len(text[: chunk.start_char].encode("utf-8"))
        byte_end = len(text[: chunk.end_char].encode("utf-8"))
        entries.append(
            ProvenanceEntry(
                chunk_id=chunk_id,
                source_kind=SourceKind.PAPER,
                order_index=chunk.order_index,
                doc_id=doc_id,
                byte_range=(byte_start, byte_end),
            )
        )
    return entries


def build_code_entries(ctx: CodeFileContext, chunks: list[Chunk]) -> list[ProvenanceEntry]:
    entries = []
    for chunk in chunks:
        chunk_id = build_code_chunk_id(ctx.repo_id, ctx.file_path, chunk.order_index)
        start_line, end_line = char_range_to_line_range(ctx.text, chunk.start_char, chunk.end_char)
        entries.append(
            ProvenanceEntry(
                chunk_id=chunk_id,
                source_kind=SourceKind.CODE,
                order_index=chunk.order_index,
                doc_id=ctx.repo_id,
                file_path=ctx.file_path,
                line_range=(start_line, end_line),
            )
        )
    return entries


def build_provenance_map(batch_id: str, entries: list[ProvenanceEntry]) -> ProvenanceMap:
    return ProvenanceMap(batch_id=batch_id, entries=entries)
