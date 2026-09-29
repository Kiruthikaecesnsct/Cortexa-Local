from dataclasses import dataclass
from typing import Protocol


@dataclass(frozen=True)
class ModelResult:
    content: str
    citations: list[str]
    model: str = ""
    provider: str = ""
    prompt_tokens: int = 0
    completion_tokens: int = 0
    total_tokens: int = 0
    finish_reason: str | None = None


class ModelRouterPort(Protocol):
    async def complete(
        self, prompt: str, evidence_refs: list[str], model: str | None = None
    ) -> ModelResult: ...
