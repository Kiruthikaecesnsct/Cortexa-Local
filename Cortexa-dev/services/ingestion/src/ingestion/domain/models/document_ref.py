from pydantic import BaseModel, ConfigDict


class DocumentRef(BaseModel):
    """Minimal read view of a document row as first written by the job-orchestrator.

    The orchestrator seeds the row with only identity, source, and lifecycle status
    (status="queued"). The richer post-ingestion fields (source_kind, chunk_count,
    provenance_map_id) do not exist yet, so ingestion must not validate the row against
    the write-side Document model. Extra fields are ignored and status is left free-form
    so the shared status vocabulary can evolve without breaking reads.
    """

    model_config = ConfigDict(extra="ignore")

    id: str
    batch_id: str
    filename: str
    status: str = "queued"
