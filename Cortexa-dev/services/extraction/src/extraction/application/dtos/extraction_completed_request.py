from dataclasses import dataclass


@dataclass
class ExtractionCompletedRequest:
    candidate_ids: list[str]
    job_id: str
    document_id: str
    trigger_type: str
    correlation_id: str
    unit_index: int = 0
    unit_count: int = 1
