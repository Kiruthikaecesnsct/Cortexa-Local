class SeedingError(Exception):
    def __init__(self, message: str, code: str) -> None:
        super().__init__(message)
        self.code = code


class UngroundedSeedingError(SeedingError):
    def __init__(self, reason: str) -> None:
        super().__init__(reason, code="UNGROUNDED_SEEDING")


class OpportunityParseError(SeedingError):
    def __init__(self, reason: str) -> None:
        super().__init__(reason, code="OPPORTUNITY_PARSE_ERROR")


class ModelRouterFailedError(SeedingError):
    def __init__(self, reason: str, status_code: int | None = None) -> None:
        super().__init__(reason, code="MODEL_ROUTER_FAILED")
        self.status_code = status_code


class VectorRouterTransientError(SeedingError):
    def __init__(self, reason: str, status_code: int | None = None) -> None:
        super().__init__(reason, code="VECTOR_ROUTER_TRANSIENT")
        self.status_code = status_code


class VectorRouterPermanentError(SeedingError):
    def __init__(self, reason: str, status_code: int | None = None) -> None:
        super().__init__(reason, code="VECTOR_ROUTER_PERMANENT")
        self.status_code = status_code


class StorageWriteError(SeedingError):
    def __init__(self, reason: str) -> None:
        super().__init__(reason, code="STORAGE_WRITE_ERROR")


class EvidenceServiceTransientError(SeedingError):
    def __init__(self, reason: str, status_code: int | None = None) -> None:
        super().__init__(reason, code="EVIDENCE_SERVICE_TRANSIENT")
        self.status_code = status_code


class EvidenceServicePermanentError(SeedingError):
    def __init__(self, reason: str, status_code: int | None = None) -> None:
        super().__init__(reason, code="EVIDENCE_SERVICE_PERMANENT")
        self.status_code = status_code


class EventPublishError(SeedingError):
    def __init__(self, reason: str) -> None:
        super().__init__(reason, code="EVENT_PUBLISH_ERROR")


class SeedingReportNotFoundError(SeedingError):
    def __init__(self, batch_id: str) -> None:
        super().__init__(
            f"Seeding report not found for batch {batch_id}", code="SEEDING_REPORT_NOT_FOUND"
        )


class IdfParseError(SeedingError):
    def __init__(self, reason: str) -> None:
        super().__init__(reason, code="IDF_PARSE_ERROR")


class AbstractTooLongError(SeedingError):
    def __init__(self, reason: str) -> None:
        super().__init__(reason, code="ABSTRACT_TOO_LONG")


class ClaimSeedParseError(SeedingError):
    def __init__(self, reason: str) -> None:
        super().__init__(reason, code="CLAIM_SEED_PARSE_ERROR")


class InsufficientClaimSeedsError(SeedingError):
    def __init__(self, reason: str) -> None:
        super().__init__(reason, code="INSUFFICIENT_CLAIM_SEEDS")


class LatticeParseError(SeedingError):
    def __init__(self, reason: str) -> None:
        super().__init__(reason, code="LATTICE_PARSE_ERROR")


class InsufficientLatticeError(SeedingError):
    def __init__(self, reason: str) -> None:
        super().__init__(reason, code="INSUFFICIENT_LATTICE")


class DigestParseError(SeedingError):
    def __init__(self, reason: str) -> None:
        super().__init__(reason, code="DIGEST_PARSE_ERROR")


class DigestGenerationError(SeedingError):
    def __init__(self, reason: str) -> None:
        super().__init__(reason, code="DIGEST_GENERATION_ERROR")


class IdeationParseError(SeedingError):
    def __init__(self, reason: str) -> None:
        super().__init__(reason, code="IDEATION_PARSE_ERROR")
