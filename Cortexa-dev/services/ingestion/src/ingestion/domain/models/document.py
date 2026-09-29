from datetime import UTC, datetime
from typing import Literal

from pydantic import BaseModel, Field

from ingestion.domain.enums.source_kind import SourceKind


class Document(BaseModel):
    id: str
    batch_id: str
    blob_uri: str
    filename: str
    source_kind: SourceKind
    provenance_map_id: str
    chunk_count: int
    status: Literal["pending", "completed"] = "pending"
    created_at: datetime = Field(default_factory=lambda: datetime.now(UTC))
    viewable_blob_uri: str | None = None
