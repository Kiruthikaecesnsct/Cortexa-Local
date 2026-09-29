from pydantic import BaseModel

from ingestion.domain.errors.provenance_errors import ChunkNotFoundError
from ingestion.domain.models.provenance_entry import ProvenanceEntry


class ProvenanceMap(BaseModel):
    batch_id: str
    entries: list[ProvenanceEntry]

    def get_by_chunk_id(self, chunk_id: str) -> ProvenanceEntry:
        for entry in self.entries:
            if entry.chunk_id == chunk_id:
                return entry
        raise ChunkNotFoundError(chunk_id)
