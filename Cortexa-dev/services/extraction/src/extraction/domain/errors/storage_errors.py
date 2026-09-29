class StorageWriteError(Exception):
    pass


class RollbackError(Exception):
    """Raised when compensating rollback itself fails — means orphaned state in infra."""


class PartialSaveError(Exception):
    """Raised when save_many fails mid-loop; carries the IDs already written."""

    def __init__(self, message: str, saved_ids: list[str]) -> None:
        super().__init__(message)
        self.saved_ids = saved_ids
