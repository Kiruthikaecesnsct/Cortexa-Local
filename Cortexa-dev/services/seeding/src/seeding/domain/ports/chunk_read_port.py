from typing import Protocol


class ChunkReadPort(Protocol):
    async def get_range(
        self, batch_id: str, document_id: str, order_start: int, order_end: int
    ) -> list[dict]: ...
