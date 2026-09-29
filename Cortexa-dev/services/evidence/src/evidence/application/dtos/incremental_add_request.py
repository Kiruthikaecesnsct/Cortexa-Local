from pydantic import BaseModel, Field

from evidence.domain.models.patent_corpus_record import PatentCorpusRecord


class IncrementalAddRequestDto(BaseModel):
    records: list[PatentCorpusRecord] = Field(min_length=1)
