from pydantic import BaseModel


class LoadCorpusResponseDto(BaseModel):
    loaded_count: int
    skipped_count: int
    failed_count: int
