from enum import StrEnum


class PatentSourceOutcome(StrEnum):
    ok = "ok"
    auth_error = "auth_error"
    rate_limited = "rate_limited"
    timeout = "timeout"
    api_error = "api_error"
    schema_error = "schema_error"
