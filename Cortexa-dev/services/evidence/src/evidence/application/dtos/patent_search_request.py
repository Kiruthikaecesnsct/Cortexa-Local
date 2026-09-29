from pydantic import BaseModel, field_validator

_MIN_LIMIT = 1
_MAX_LIMIT = 50
_DEFAULT_LIMIT = 25


class PatentSearchRequestDto(BaseModel):
    query: str
    limit: int = _DEFAULT_LIMIT

    @field_validator("query")
    @classmethod
    def _validate_query(cls, v: str) -> str:
        if not v.strip():
            raise ValueError("query must not be empty or whitespace")
        return v.strip()

    @field_validator("limit")
    @classmethod
    def _clamp_limit(cls, v: int) -> int:
        return max(_MIN_LIMIT, min(_MAX_LIMIT, v))
