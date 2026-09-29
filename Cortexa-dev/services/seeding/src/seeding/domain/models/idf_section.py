from pydantic import BaseModel


class IdfSection(BaseModel):
    text: str
    citations: list[str]
