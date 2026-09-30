import hashlib
from collections.abc import Iterator
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path

from pydantic import BaseModel, Field

from ingestion.domain.enums.clone_status import CloneStatus


class RepositoryClone(BaseModel):
    clone_id: str
    owner: str
    repository: str
    branch: str
    status: CloneStatus
    created_at: datetime
    updated_at: datetime
    size_bytes: int | None = None
    commit_sha: str | None = None
    error: str | None = None
    # Id of the user who started the save; kept for auditing and never returned to clients.
    saved_by: str = Field(default="", exclude=True)


def clone_id_for(owner: str, repository: str, branch: str) -> str:
    """Stable id for one saved branch; the same branch always maps to the same stored zip."""
    return hashlib.sha256(f"{owner}/{repository}/{branch}".encode()).hexdigest()[:32]


@dataclass(frozen=True)
class PackedArchive:
    path: Path
    commit_sha: str


@dataclass(frozen=True)
class ArchiveDownload:
    chunks: Iterator[bytes]
    size_bytes: int
    filename: str
