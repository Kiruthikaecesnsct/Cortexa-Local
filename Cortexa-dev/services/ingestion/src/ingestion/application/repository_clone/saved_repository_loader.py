import tempfile
from collections.abc import AsyncIterator, Mapping
from contextlib import asynccontextmanager
from pathlib import Path

from ingestion.application.repository_clone.clone_ports import RepositorySource, SavedFolderReader
from ingestion.domain.enums.source_provider import SourceProvider
from ingestion.domain.models.document_ref import SavedRepositoryRef
from ingestion.infrastructure.git.clone_cleanup import remove_clone


class SavedRepositoryLoader:
    """Copies a saved branch folder to a temporary directory for ingestion.

    Names are re-validated with the provider's rules, so a document row can only point
    at a real saved-branch folder. The copy lives under the clone workdir, so the
    startup sweep removes it if the process dies before cleanup.
    """

    def __init__(
        self,
        reader: SavedFolderReader,
        sources: Mapping[SourceProvider, RepositorySource],
        workdir: str,
    ) -> None:
        self._reader = reader
        self._sources = sources
        self._workdir = workdir

    @asynccontextmanager
    async def materialize(self, ref: SavedRepositoryRef) -> AsyncIterator[Path]:
        target = self._sources[ref.provider].target_from_names(
            ref.owner, ref.repository, ref.branch
        )
        Path(self._workdir).mkdir(parents=True, exist_ok=True)
        folder = Path(tempfile.mkdtemp(prefix="saved-", dir=self._workdir))
        try:
            await self._reader.fetch_folder(target, folder, ref.selected_files)
            yield folder
        finally:
            remove_clone(str(folder))
