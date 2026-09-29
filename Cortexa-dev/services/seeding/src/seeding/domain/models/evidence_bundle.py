from pydantic import BaseModel, field_validator

from seeding.domain.validation.prompt_injection import reject_prompt_delimiters


class EvidenceHit(BaseModel):
    source_id: str
    text: str
    start: int
    end: int

    @field_validator("text", "source_id", mode="before")
    @classmethod
    def _reject_prompt_delimiters(cls, v: object) -> object:
        return reject_prompt_delimiters(v)


class EvidenceBundle(BaseModel):
    id: str
    batch_id: str
    job_id: str
    candidate_id: str
    document_id: str
    hits: list[EvidenceHit]
    source_flags: list[str]
