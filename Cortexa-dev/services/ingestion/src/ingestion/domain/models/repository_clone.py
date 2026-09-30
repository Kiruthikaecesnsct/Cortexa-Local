import hashlib
from collections.abc import Iterator
from dataclasses import dataclass, field
from datetime import datetime
from pathlib import Path

from pydantic import BaseModel, Field

from ingestion.domain.enums.clone_status import CloneStatus
from ingestion.domain.enums.git_host import GitHost
from ingestion.domain.enums.source_provider import SourceProvider


@dataclass(frozen=True)
class SaveTarget:
    """One branch to save.

    `owner` is the GitHub owner or Azure DevOps organization. `repository` is the
    GitHub repository name, or the Azure DevOps "{project}/{repository}" handle.
    """

    provider: SourceProvider
    owner: str
    repository: str
    branch: str

    @property
    def repository_name(self) -> str:
        return self.repository.rsplit("/", 1)[-1]

    @property
    def clone_id(self) -> str:
        """Stable id: the same branch always maps to the same stored zip."""
        key = f"{self.provider}/{self.owner}/{self.repository}/{self.branch}"
        return hashlib.sha256(key.encode()).hexdigest()[:32]


class RepositoryClone(BaseModel):
    clone_id: str
    provider: SourceProvider
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

    @property
    def target(self) -> SaveTarget:
        return SaveTarget(self.provider, self.owner, self.repository, self.branch)


@dataclass(frozen=True)
class CheckoutSpec:
    """What the git workspace needs to clone one branch."""

    host: GitHost
    url: str
    branch: str
    token: str = field(repr=False)


@dataclass(frozen=True)
class PackedArchive:
    path: Path
    commit_sha: str


@dataclass(frozen=True)
class ArchiveDownload:
    chunks: Iterator[bytes]
    size_bytes: int
    filename: str
