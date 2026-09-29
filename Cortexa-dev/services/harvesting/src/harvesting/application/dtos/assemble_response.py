from pydantic import BaseModel


class AssembleResponseDto(BaseModel):
    report_id: str
    candidate_count: int
