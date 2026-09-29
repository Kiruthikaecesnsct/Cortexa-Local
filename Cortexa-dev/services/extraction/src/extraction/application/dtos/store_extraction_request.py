from pydantic import BaseModel, field_validator

from extraction.domain.models.invention_candidate import InventionCandidate


class StoreExtractionRequest(BaseModel):
    candidates: list[InventionCandidate]
    job_id: str
    document_id: str
    trigger_type: str
    correlation_id: str | None = None

    @field_validator("trigger_type")
    @classmethod
    def trigger_type_non_empty(cls, v: str) -> str:
        if not v.strip():
            raise ValueError("trigger_type must not be empty")
        return v
