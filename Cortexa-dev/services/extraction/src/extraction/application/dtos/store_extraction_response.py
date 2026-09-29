from pydantic import BaseModel


class StoreExtractionResponse(BaseModel):
    document_id: str
    candidate_count: int
    candidate_ids: list[str]
