from pydantic import BaseModel, model_validator

from ingestion.domain.enums.source_kind import SourceKind
from ingestion.domain.errors.provenance_errors import InvalidProvenanceEntryError


class ProvenanceEntry(BaseModel):
    chunk_id: str
    source_kind: SourceKind
    order_index: int
    doc_id: str
    byte_range: tuple[int, int] | None = None
    file_path: str | None = None
    line_range: tuple[int, int] | None = None

    @model_validator(mode="after")
    def validate_kind_fields(self) -> ProvenanceEntry:
        if self.source_kind == SourceKind.PAPER and self.byte_range is None:
            raise InvalidProvenanceEntryError("byte_range is required for PAPER provenance entries")
        if self.source_kind == SourceKind.CODE and (
            self.file_path is None or self.line_range is None
        ):
            raise InvalidProvenanceEntryError(
                "file_path and line_range are required for CODE provenance entries"
            )
        return self
