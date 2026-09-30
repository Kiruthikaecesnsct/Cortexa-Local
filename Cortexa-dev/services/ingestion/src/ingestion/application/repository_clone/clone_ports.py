from typing import Protocol

from pydantic import SecretStr

from ingestion.domain.enums.source_provider import SourceProvider
from ingestion.domain.models.repository_clone import (
    ArchiveDownload,
    CheckoutSpec,
    PackedArchive,
    RepositoryClone,
    SaveTarget,
)


class SaveRequest(Protocol):
    """The fields every provider's save request carries."""

    @property
    def org_url(self) -> str: ...

    @property
    def repository(self) -> str: ...

    @property
    def branch(self) -> str: ...

    @property
    def pat(self) -> SecretStr: ...


class RepositorySource(Protocol):
    """Provider-specific rules for saving a branch: validation, size, and clone URL."""

    @property
    def provider(self) -> SourceProvider: ...

    def target_from_request(self, request: SaveRequest) -> SaveTarget: ...

    def target_from_names(self, owner: str, repository: str, branch: str) -> SaveTarget: ...

    async def repository_size_bytes(self, target: SaveTarget, token: str) -> int: ...

    def checkout_spec(self, target: SaveTarget, token: str) -> CheckoutSpec: ...


class RepositoryWorkspace(Protocol):
    """Clones a branch and packs its tracked files into a zip archive."""

    async def clone_and_pack(self, spec: CheckoutSpec) -> PackedArchive: ...

    def discard(self, archive: PackedArchive) -> None: ...


class CloneArchiveStore(Protocol):
    """Object storage for saved branches, one zip per provider/owner/repository/branch."""

    async def ensure_ready(self) -> None: ...

    async def save(self, archive: PackedArchive, clone: RepositoryClone) -> int: ...

    async def list_clones(self, provider: SourceProvider) -> list[RepositoryClone]: ...

    async def open_download(self, target: SaveTarget) -> ArchiveDownload: ...
