from typing import Protocol, runtime_checkable

from extraction.domain.events.event_envelope import EventEnvelope
from extraction.domain.models.invention_candidate import InventionCandidate


@runtime_checkable
class CandidateRepository(Protocol):
    async def save_many(self, candidates: list[InventionCandidate]) -> list[str]: ...

    async def delete_many(self, batch_id: str, candidate_ids: list[str]) -> None: ...


@runtime_checkable
class EventPublisher(Protocol):
    async def publish(self, topic: str, event: EventEnvelope) -> None: ...
