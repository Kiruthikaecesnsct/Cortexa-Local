class ScanError(Exception):
    """Base for every scan and save error; `code` is returned to clients."""

    code = "scan_error"


class RepositoryTooLargeError(ScanError):
    code = "repository_too_large"


class MissingUserContextError(ScanError):
    code = "missing_user_context"


class CloneNotFoundError(ScanError):
    code = "clone_not_found"


class CloneStorageUnavailableError(ScanError):
    code = "clone_storage_unavailable"


class CloneExecutionError(ScanError):
    code = "clone_failed"
