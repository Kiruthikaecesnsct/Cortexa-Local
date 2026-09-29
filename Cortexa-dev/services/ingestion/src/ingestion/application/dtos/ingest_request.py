from pydantic import BaseModel

from ingestion.domain.enums.source_kind import SourceKind
from ingestion.domain.models.chunk import Chunk
from ingestion.domain.models.provenance_map import ProvenanceMap


class IngestRequest(BaseModel):
    batch_id: str
    document_id: str
    filename: str
    source_kind: SourceKind
    raw_content: bytes
    chunks: list[Chunk]
    provenance_map: ProvenanceMap
    correlation_id: str
    viewable_pdf: bytes | None = None
    geometry_reason: str | None = None
