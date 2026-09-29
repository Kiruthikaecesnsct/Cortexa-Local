from typing import Protocol

from pydantic import BaseModel

from evidence.domain.events.event_envelope import EventEnvelope
from evidence.domain.models.evidence_bundle import EvidenceBundle
from evidence.domain.models.patent_corpus_record import PatentCorpusRecord


class CandidateRecord(BaseModel):
    id: str
    batch_id: str
    document_id: str
    claim_text: str
    problem: str
    tech_field: str


class EvidenceBundleRepository(Protocol):
    async def save(self, bundle: EvidenceBundle) -> None: ...

    async def find_for_candidate(
        self, batch_id: str, document_id: str, candidate_id: str
    ) -> EvidenceBundle | None: ...


class EventPublisher(Protocol):
    async def publish(self, topic: str, event: EventEnvelope) -> None: ...


class CorpusLoader(Protocol):
    async def embed_and_upsert(self, records: list[PatentCorpusRecord]) -> int: ...


class CorpusReader(Protocol):
    def read(self, file_path: str | None = None) -> list[PatentCorpusRecord]: ...


class CandidateReader(Protocol):
    async def get_by_candidate_id(self, candidate_id: str, batch_id: str) -> CandidateRecord: ...
