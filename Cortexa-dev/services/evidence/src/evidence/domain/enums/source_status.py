from enum import StrEnum


class SourceStatus(StrEnum):
    active = "active"
    empty = "empty"
    filtered = "filtered"
    error = "error"
    timeout = "timeout"
