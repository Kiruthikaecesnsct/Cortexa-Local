from pydantic import BaseModel

from evidence.domain.models.evidence_bundle import EvidenceBundle


class StoreEvidenceRequestDto(BaseModel):
    bundle: EvidenceBundle
    correlation_id: str | None = None
