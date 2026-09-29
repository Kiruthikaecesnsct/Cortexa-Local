from pydantic import AliasChoices, BaseModel, Field


class TokenUsage(BaseModel, frozen=True):
    # The model-router (.NET) serializes the nested usage object in camelCase
    # (promptTokens/…) via ASP.NET's default policy; accept both that and snake_case.
    prompt_tokens: int = Field(validation_alias=AliasChoices("prompt_tokens", "promptTokens"))
    completion_tokens: int = Field(
        validation_alias=AliasChoices("completion_tokens", "completionTokens")
    )
    total_tokens: int = Field(validation_alias=AliasChoices("total_tokens", "totalTokens"))


class ModelCompleteResult(BaseModel, frozen=True):
    provider: str
    model: str
    content: str
    citations: list[str] | None = None
    usage: TokenUsage | None = None
    grounding: dict | None = None
    error: str | None = None
    finish_reason: str | None = Field(
        default=None,
        validation_alias=AliasChoices("finish_reason", "finishreason"),
    )


class DualModelCompleteResult(BaseModel, frozen=True):
    primary: ModelCompleteResult
    secondary: ModelCompleteResult
