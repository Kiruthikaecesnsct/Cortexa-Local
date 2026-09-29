from typing import Protocol


class CandidateWritePort(Protocol):
    async def upsert_many(self, candidates: list[dict]) -> None: ...
