from enum import StrEnum


class CloneStatus(StrEnum):
    QUEUED = "queued"
    CLONING = "cloning"
    UPLOADING = "uploading"
    STORED = "stored"
    FAILED = "failed"

    @property
    def in_progress(self) -> bool:
        return self in {CloneStatus.QUEUED, CloneStatus.CLONING, CloneStatus.UPLOADING}
