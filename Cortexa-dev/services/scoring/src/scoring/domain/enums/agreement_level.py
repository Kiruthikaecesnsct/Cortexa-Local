from enum import StrEnum


class AgreementLevel(StrEnum):
    Full = "full"
    Partial = "partial"
    NoAgreement = "no_agreement"
    FallbackSingle = "fallback_single"
    SingleConfigured = "single_configured"
