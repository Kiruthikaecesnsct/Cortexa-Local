from typing import Any, Protocol

from scoring.domain.events.event_envelope import EventEnvelope
from scoring.domain.models.evidence_bundle import EvidenceBundle
from scoring.domain.models.stored_verdict import StoredVerdict


class VerdictRepository(Protocol):
    async def save(self, verdict: StoredVerdict) -> None: ...

    async def find_by_candidate(self, candidate_id: str, batch_id: str) -> StoredVerdict | None: ...


class EventPublisher(Protocol):
    async def publish(self, topic: str, event: EventEnvelope) -> None: ...


class EvidenceBundleReader(Protocol):
    async def get_domain_bundle(
        self, candidate_id: str, batch_id: str
    ) -> EvidenceBundle | None: ...


class CandidateReader(Protocol):
    async def get_by_candidate_id(self, candidate_id: str, batch_id: str) -> Any: ...
