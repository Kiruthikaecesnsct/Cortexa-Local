from datetime import UTC, datetime

from pydantic import BaseModel, Field

from harvesting.domain.models.report_candidate import ReportCandidate


class HarvestingReport(BaseModel):
    id: str
    batch_id: str
    document_id: str
    engine: str = "harvesting"
    generated_at: datetime = Field(default_factory=lambda: datetime.now(UTC))
    candidates: list[ReportCandidate]
