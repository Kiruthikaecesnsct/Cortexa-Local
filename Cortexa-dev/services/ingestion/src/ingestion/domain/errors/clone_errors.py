class CloneError(Exception):
    pass


class InvalidRepoUrlError(CloneError):
    pass


class UnsupportedHostError(CloneError):
    pass


class RepoTooLargeError(CloneError):
    def __init__(self, size_bytes: int, limit_bytes: int) -> None:
        super().__init__(f"Repository size {size_bytes} bytes exceeds limit of {limit_bytes} bytes")
        self.size_bytes = size_bytes
        self.limit_bytes = limit_bytes


class CloneTimeoutError(CloneError):
    def __init__(self, timeout_seconds: float) -> None:
        super().__init__(f"Clone operation timed out after {timeout_seconds} seconds")
        self.timeout_seconds = timeout_seconds


class AuthResolutionError(CloneError):
    pass


class GitExecutionError(CloneError):
    def __init__(self, exit_code: int, message: str) -> None:
        super().__init__(f"Git exited with code {exit_code}: {message}")
        self.exit_code = exit_code
