from pydantic import BaseModel


class StoreVerdictResponseDto(BaseModel):
    verdict_id: str
    document_id: str
    published: bool
