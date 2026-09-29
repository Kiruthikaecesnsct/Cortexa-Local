class ModelCallFailed(Exception):
    def __init__(
        self,
        message: str,
        status_code: int | None = None,
        error_code: str | None = None,
        categories: list[str] | None = None,
    ) -> None:
        super().__init__(message)
        self.status_code = status_code
        self.error_code = error_code
        self.categories = categories


class PromptBuildError(Exception):
    def __init__(self, message: str) -> None:
        super().__init__(message)


class CandidateParseError(Exception):
    def __init__(self, message: str) -> None:
        super().__init__(message)
