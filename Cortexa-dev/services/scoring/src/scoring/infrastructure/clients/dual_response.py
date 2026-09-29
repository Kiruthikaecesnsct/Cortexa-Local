from pydantic import BaseModel


class ModelResult(BaseModel):
    provider: str
    model: str
    content: str
    error: str | None = None


class DualCompleteResponse(BaseModel):
    primary: ModelResult
    secondary: ModelResult
