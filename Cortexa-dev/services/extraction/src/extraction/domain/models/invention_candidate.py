from datetime import datetime

from pydantic import BaseModel

from extraction.domain.value_objects.provenance_span import ProvenanceSpan


class InventionCandidate(BaseModel, frozen=True):
    id: str
    document_id: str
    batch_id: str
    claim_text: str
    problem: str
    mechanism: str
    tech_field: str
    ipc_cpc_guess: str | None
    source_span: ProvenanceSpan
    source_chunk_index: int
    created_at: datetime
    extraction_unit_index: int = 0
