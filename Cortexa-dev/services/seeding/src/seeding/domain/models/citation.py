from pydantic import BaseModel


class Citation(BaseModel):
    ref: str
    source_type: str
    title: str
    patent_id: str | None = None
    url: str = ""
    similarity: float = 0.0
