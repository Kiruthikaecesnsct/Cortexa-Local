from pydantic import BaseModel

from extraction.domain.value_objects.provenance_span import ProvenanceSpan


class ChunkInput(BaseModel, frozen=True):
    text: str
    order_index: int
    source_span: ProvenanceSpan
    document_id: str
