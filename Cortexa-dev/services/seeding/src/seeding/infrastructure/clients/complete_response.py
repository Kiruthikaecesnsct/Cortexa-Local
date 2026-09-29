from pydantic import AliasChoices, BaseModel, Field


class TokenUsage(BaseModel):
    prompt_tokens: int = Field(
        default=0, validation_alias=AliasChoices("prompt_tokens", "promptTokens")
    )
    completion_tokens: int = Field(
        default=0, validation_alias=AliasChoices("completion_tokens", "completionTokens")
    )
    total_tokens: int = Field(
        default=0, validation_alias=AliasChoices("total_tokens", "totalTokens")
    )


class CompleteResponse(BaseModel):
    provider: str
    content: str
    citations: list[str] | None = None
    model: str = ""
    usage: TokenUsage | None = None
    finish_reason: str | None = Field(
        default=None, validation_alias=AliasChoices("finish_reason", "finishReason")
    )
