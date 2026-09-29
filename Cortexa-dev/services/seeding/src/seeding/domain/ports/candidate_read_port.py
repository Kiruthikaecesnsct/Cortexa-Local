from typing import Protocol


class CandidateReadPort(Protocol):
    async def get_by_batch(self, batch_id: str) -> list[dict]: ...

    async def get_seeded_by_batch(self, batch_id: str) -> list[dict]: ...
