class EvidenceError(Exception):
    pass


class PatentApiError(EvidenceError):
    def __init__(self, message: str, status_code: int | None = None) -> None:
        super().__init__(message)
        self.status_code = status_code


class PatentAuthError(PatentApiError):
    pass


class CorpusSearchError(EvidenceError):
    def __init__(self, message: str, status_code: int | None = None) -> None:
        super().__init__(message)
        self.status_code = status_code


class CorpusLoadError(EvidenceError):
    def __init__(
        self,
        message: str,
        status_code: int | None = None,
        loaded_count: int = 0,
    ) -> None:
        super().__init__(message)
        self.status_code = status_code
        self.loaded_count = loaded_count


class LlmResearchError(EvidenceError):
    def __init__(
        self,
        message: str,
        status_code: int | None = None,
        content_filter: bool = False,
    ) -> None:
        super().__init__(message)
        self.status_code = status_code
        self.content_filter = content_filter


class SecretResolutionError(EvidenceError):
    def __init__(self, secret_name: str, reason: str) -> None:
        super().__init__(f"Failed to resolve secret '{secret_name}': {reason}")
        self.secret_name = secret_name


class EvidenceBundleSourceUnavailableError(EvidenceError):
    pass


class EvidenceBundleBelowMinimumSourcesError(EvidenceError):
    pass


class EvidenceCandidateDeadlineExceededError(EvidenceError):
    def __init__(self, pending_sources: list[str], elapsed_seconds: float) -> None:
        message = (
            f"Candidate deadline exceeded after {elapsed_seconds:.1f}s; "
            f"pending sources={pending_sources}"
        )
        super().__init__(message)
        self.pending_sources = pending_sources
        self.elapsed_seconds = elapsed_seconds


class StorageWriteError(EvidenceError):
    pass


class EventPublishError(EvidenceError):
    pass


class CandidateNotFoundError(EvidenceError):
    def __init__(self, candidate_id: str) -> None:
        super().__init__(f"Candidate not found: {candidate_id}")
        self.candidate_id = candidate_id
