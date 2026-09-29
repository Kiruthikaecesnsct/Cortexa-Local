from pydantic import BaseModel

from ingestion.domain.events.event_envelope import EventEnvelope


class IngestionCompletedPayload(BaseModel):
    document_id: str
    blob_uri: str
    chunk_count: int
    provenance_map_id: str


def make_ingestion_completed_event(
    batch_id: str,
    document_id: str,
    blob_uri: str,
    chunk_count: int,
    provenance_map_id: str,
    correlation_id: str,
) -> EventEnvelope:
    payload = IngestionCompletedPayload(
        document_id=document_id,
        blob_uri=blob_uri,
        chunk_count=chunk_count,
        provenance_map_id=provenance_map_id,
    )
    return EventEnvelope(
        event_type="ingestion.completed",
        batch_id=batch_id,
        document_id=document_id,
        correlation_id=correlation_id,
        payload=payload.model_dump(),
    )
