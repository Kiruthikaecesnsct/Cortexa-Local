from pydantic import BaseModel, Field

ScalarValue = str | int | float | bool


class PatentCorpusRecord(BaseModel):
    reference: str
    title: str
    abstract: str
    applicant: str
    date: str
    url: str
    metadata: dict[str, ScalarValue] = Field(default_factory=dict)

    def embedding_text(self) -> str:
        return f"{self.title}. {self.abstract}".strip()
