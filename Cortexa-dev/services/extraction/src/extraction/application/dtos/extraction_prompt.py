from pydantic import BaseModel


class PromptOptions(BaseModel, frozen=True):
    max_tokens: int = 16384
    temperature: float = 0.2


class ExtractionPrompt(BaseModel, frozen=True):
    task_kind: str
    prompt: str
    evidence_refs: list[str] | None
    options: PromptOptions
