import re

from pydantic import BaseModel, Field, field_validator

from scoring.domain.models.evidence_bundle import EvidenceBundle

_DIRECTIVE_PATTERN = re.compile(
    r"^\s*(ignore|system:|###|<\|im_start\||<\|im_end\||<\|system\|)",
    re.IGNORECASE | re.MULTILINE,
)


class ScoreVerdictRequestDto(BaseModel):
    candidate_description: str = Field(..., min_length=1, max_length=4000)
    bundle: EvidenceBundle
    correlation_id: str | None = None
    ai_model: str | None = None

    @field_validator("candidate_description")
    @classmethod
    def _reject_directives(cls, v: str) -> str:
        if _DIRECTIVE_PATTERN.search(v):
            raise ValueError("candidate_description contains disallowed directive patterns")
        return v
