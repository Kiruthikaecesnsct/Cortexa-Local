class StorageWriteError(Exception):
    pass


class RollbackError(Exception):
    """Raised when compensating rollback itself fails — means orphaned state in infra."""
