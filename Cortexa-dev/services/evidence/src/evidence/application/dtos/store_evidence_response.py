from pydantic import BaseModel


class StoreEvidenceResponseDto(BaseModel):
    bundle_id: str
    document_id: str
    published: bool
