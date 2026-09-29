from ingestion.application.chunker import chunk_text
from ingestion.application.dtos.ingest_request import IngestRequest
from ingestion.application.provenance.provenance_builder import (
    CodeFileContext,
    build_code_entries,
    build_provenance_map,
)
from ingestion.application.raw_file_ingestor import IngestorConfig
from ingestion.domain.enums.source_kind import SourceKind


def assemble_code_ingest_request(
    batch_id: str,
    document_id: str,
    files: list[tuple[str, str]],
    correlation_id: str,
    config: IngestorConfig,
) -> IngestRequest:
    all_entries = []
    all_chunks = []
    order_offset = 0

    for file_path, text in files:
        chunks = chunk_text(
            text,
            size=config.chunk_size,
            overlap=config.chunk_overlap,
            encoding_name=config.chunk_encoding,
        )
        for chunk in chunks:
            chunk.order_index += order_offset

        ctx = CodeFileContext(repo_id=document_id, file_path=file_path, text=text)
        entries = build_code_entries(ctx, chunks)

        all_chunks.extend(chunks)
        all_entries.extend(entries)
        order_offset += len(chunks)

    provenance_map = build_provenance_map(batch_id, all_entries)

    return IngestRequest(
        batch_id=batch_id,
        document_id=document_id,
        filename="",
        source_kind=SourceKind.CODE,
        raw_content=b"",
        chunks=all_chunks,
        provenance_map=provenance_map,
        correlation_id=correlation_id,
    )
