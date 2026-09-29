from typing import Protocol

from harvesting.domain.events.event_envelope import EventEnvelope
from harvesting.domain.models.engine_completed_event import EngineCompletedEvent
from harvesting.domain.models.harvesting_report import HarvestingReport
from harvesting.domain.models.maturity_result import MaturityResult


class MaturityResultRepository(Protocol):
    async def save(self, result: MaturityResult) -> None: ...


class ReportRepository(Protocol):
    async def save(self, report: HarvestingReport) -> None: ...
    async def get_by_batch(self, batch_id: str) -> HarvestingReport: ...


class VerdictReadRepository(Protocol):
    async def get_by_batch(self, batch_id: str) -> list[dict]: ...


class CandidateReadRepository(Protocol):
    async def get_by_batch(self, batch_id: str) -> list[dict]: ...


class EvidenceBundleReadRepository(Protocol):
    async def get_by_batch(self, batch_id: str) -> list[dict]: ...


class EventPublisher(Protocol):
    async def publish(self, event: EngineCompletedEvent) -> None: ...


class FailedEventPublisher(Protocol):
    async def publish(self, topic: str, event: EventEnvelope) -> None: ...
