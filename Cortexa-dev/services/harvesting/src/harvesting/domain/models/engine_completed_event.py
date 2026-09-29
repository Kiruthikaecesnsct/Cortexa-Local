from datetime import datetime

from pydantic import BaseModel


class EngineCompletedEvent(BaseModel):
    event_type: str = "engine.completed"
    schema_version: str = "1.0"
    batch_id: str
    document_id: str
    correlation_id: str
    occurred_at: datetime
    payload: dict[str, str]
