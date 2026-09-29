from pydantic import BaseModel


class IngestResponse(BaseModel):
    document_id: str
    blob_uri: str
    chunk_count: int
    provenance_map_id: str
