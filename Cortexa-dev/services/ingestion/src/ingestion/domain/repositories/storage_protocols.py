from typing import Protocol, runtime_checkable

from ingestion.domain.events.event_envelope import EventEnvelope
from ingestion.domain.models.chunk import Chunk
from ingestion.domain.models.document import Document
from ingestion.domain.models.document_ref import DocumentRef
from ingestion.domain.models.provenance_map import ProvenanceMap


@runtime_checkable
class BlobRepository(Protocol):
    def build_uri(self, batch_id: str, document_id: str) -> str: ...

    async def save(self, batch_id: str, document_id: str, content: bytes) -> str: ...

    async def delete(self, blob_uri: str) -> None: ...

    async def read(self, batch_id: str, document_id: str) -> tuple[bytes, str]: ...


@runtime_checkable
class DocumentRepository(Protocol):
    async def save(self, document: Document) -> None: ...

    async def delete(self, document_id: str, batch_id: str) -> None: ...

    async def get(self, document_id: str, batch_id: str) -> DocumentRef | None: ...

    async def mark_completed(self, document_id: str, batch_id: str) -> None: ...


@runtime_checkable
class ChunkRepository(Protocol):
    async def save_many(
        self, batch_id: str, document_id: str, chunks_with_ids: list[tuple[str, Chunk]]
    ) -> None: ...

    async def delete_many(self, batch_id: str, chunk_ids: list[str]) -> None: ...

    async def exists(self, batch_id: str, document_id: str) -> bool: ...


@runtime_checkable
class ProvenanceRepository(Protocol):
    async def save(self, provenance_map: ProvenanceMap) -> None: ...

    async def delete_map(self, batch_id: str, chunk_ids: list[str]) -> None: ...


@runtime_checkable
class EventPublisher(Protocol):
    async def publish(self, topic: str, event: EventEnvelope) -> None: ...
