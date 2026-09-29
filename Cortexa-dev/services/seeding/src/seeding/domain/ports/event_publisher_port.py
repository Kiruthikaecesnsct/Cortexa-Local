from typing import Protocol


class EventPublisherPort(Protocol):
    async def publish(
        self,
        topic: str,
        payload: dict,
        session_id: str | None = None,
        correlation_id: str | None = None,
    ) -> None: ...
