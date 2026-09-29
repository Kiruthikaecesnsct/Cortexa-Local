class StorageError(Exception):
    pass


class StorageWriteError(StorageError):
    pass


class VerdictNotFoundError(StorageError):
    def __init__(self, candidate_id: str) -> None:
        super().__init__(f"Verdict not found for candidate_id: {candidate_id}")
        self.candidate_id = candidate_id


class EventPublishError(StorageError):
    def __init__(
        self,
        message: str = "",
        verdict_id: str | None = None,
        document_id: str | None = None,
    ) -> None:
        super().__init__(message)
        self.verdict_id = verdict_id
        self.document_id = document_id
