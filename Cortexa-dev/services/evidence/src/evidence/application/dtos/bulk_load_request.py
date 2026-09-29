from pydantic import BaseModel

from evidence.domain.models.patent_corpus_record import PatentCorpusRecord


class BulkLoadRequestDto(BaseModel):
    file_path: str | None = None
    records: list[PatentCorpusRecord] | None = None
