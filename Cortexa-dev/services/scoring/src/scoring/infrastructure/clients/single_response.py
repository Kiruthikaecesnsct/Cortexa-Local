from pydantic import BaseModel


class SingleCompleteResponse(BaseModel):
    provider: str
    model: str
    content: str
    error: str | None = None
