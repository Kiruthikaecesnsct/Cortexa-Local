from typing import Protocol

from seeding.domain.models.seeding_result import SeedingResult


class SeedingReportRepositoryPort(Protocol):
    async def save(self, result: SeedingResult) -> None: ...

    async def get_by_batch(self, batch_id: str) -> SeedingResult: ...
