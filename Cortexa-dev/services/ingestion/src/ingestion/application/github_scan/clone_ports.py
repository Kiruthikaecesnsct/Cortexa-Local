from typing import Protocol

from ingestion.domain.models.repository_clone import (
    ArchiveDownload,
    PackedArchive,
    RepositoryClone,
)


class RepositoryWorkspace(Protocol):
    """Clones a branch and packs its tracked files into a zip archive."""

    async def clone_and_pack(
        self, owner: str, repo: str, branch: str, token: str
    ) -> PackedArchive: ...

    def discard(self, archive: PackedArchive) -> None: ...


class CloneArchiveStore(Protocol):
    """Object storage for saved branches, one zip per owner/repository/branch."""

    async def ensure_ready(self) -> None: ...

    async def save(self, archive: PackedArchive, clone: RepositoryClone) -> int: ...

    async def list_clones(self) -> list[RepositoryClone]: ...

    async def open_download(self, owner: str, repository: str, branch: str) -> ArchiveDownload: ...
