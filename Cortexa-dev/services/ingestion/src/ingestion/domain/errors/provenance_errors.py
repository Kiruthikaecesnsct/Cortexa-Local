class ProvenanceError(Exception):
    pass


class InvalidProvenanceEntryError(ProvenanceError):
    pass


class ChunkNotFoundError(ProvenanceError):
    def __init__(self, chunk_id: str) -> None:
        super().__init__(f"Chunk not found: {chunk_id}")
        self.chunk_id = chunk_id
