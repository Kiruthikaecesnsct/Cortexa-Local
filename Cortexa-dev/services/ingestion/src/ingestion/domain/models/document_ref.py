from pydantic import BaseModel, ConfigDict, Field

from ingestion.domain.enums.source_provider import SourceProvider


class SavedRepositoryRef(BaseModel):
    """A branch saved to repository storage that a document should be ingested from."""

    model_config = ConfigDict(extra="ignore", frozen=True)

    provider: SourceProvider
    owner: str = Field(min_length=1, max_length=64)
    repository: str = Field(min_length=1, max_length=129)
    branch: str = Field(min_length=1, max_length=255)


class DocumentRef(BaseModel):
    """Minimal read view of a document row as first written by the job-orchestrator.

    The orchestrator seeds the row with only identity, source, and lifecycle status
    (status="queued"). The richer post-ingestion fields (source_kind, chunk_count,
    provenance_map_id) do not exist yet, so ingestion must not validate the row against
    the write-side Document model. Extra fields are ignored and status is left free-form
    so the shared status vocabulary can evolve without breaking reads.

    `saved_repository` is set when the document is a saved repository folder rather
    than an uploaded file or a repository URL.
    """

    model_config = ConfigDict(extra="ignore")

    id: str
    batch_id: str
    filename: str
    status: str = "queued"
    saved_repository: SavedRepositoryRef | None = None
