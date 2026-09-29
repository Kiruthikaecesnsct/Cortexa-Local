class DomainError(Exception):
    def __init__(self, message: str, code: str) -> None:
        super().__init__(message)
        self.code = code


class StorageWriteError(DomainError):
    def __init__(self, reason: str) -> None:
        super().__init__(reason, code="STORAGE_WRITE_ERROR")


class AxisMissingError(DomainError):
    def __init__(self, axis: str) -> None:
        super().__init__(f"required axis '{axis}' not present in candidate", code="AXIS_MISSING")


class EventPublishError(DomainError):
    def __init__(self, reason: str) -> None:
        super().__init__(reason, code="EVENT_PUBLISH_ERROR")


class ReportNotFoundError(DomainError):
    def __init__(self, batch_id: str) -> None:
        super().__init__(
            f"harvesting report not found for batch {batch_id}", code="REPORT_NOT_FOUND"
        )


class StoragePermanentError(DomainError):
    def __init__(self, reason: str) -> None:
        super().__init__(reason, code="STORAGE_PERMANENT_ERROR")
