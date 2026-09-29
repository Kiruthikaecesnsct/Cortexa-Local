class ScoringError(Exception):
    def __init__(self, message: str, code: str) -> None:
        super().__init__(message)
        self.code = code


class UngroundedVerdictError(ScoringError):
    def __init__(
        self, reason: str = "evidence bundle does not meet grounding requirements"
    ) -> None:
        super().__init__(reason, code="UNGROUNDED_VERDICT")


class AxisParseError(ScoringError):
    def __init__(self, reason: str) -> None:
        super().__init__(reason, code="AXIS_PARSE_ERROR")


class DualScoringFailedError(ScoringError):
    def __init__(self, reason: str = "both models failed", status_code: int | None = None) -> None:
        super().__init__(reason, code="DUAL_SCORING_FAILED")
        self.status_code = status_code


class SingleScoringFailedError(ScoringError):
    def __init__(self, reason: str = "model call failed", status_code: int | None = None) -> None:
        super().__init__(reason, code="SINGLE_SCORING_FAILED")
        self.status_code = status_code
