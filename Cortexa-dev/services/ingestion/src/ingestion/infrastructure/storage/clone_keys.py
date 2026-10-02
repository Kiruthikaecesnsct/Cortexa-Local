"""Object key layout for saved branches.

Files:  {provider}/{owner}/{repository}/{branch}/{path inside the repository}
Marker: _saved/{provider}/{owner}/{repository}/{branch}.json

GitHub repositories are "repo"; Azure DevOps repositories are "project/repo". A branch
with slashes becomes nested folders. The marker lists the saved files, so listing and
replacing a branch never has to scan or delete by prefix (branch "a" shares the
prefix of branch "a/b").
"""

from pathlib import Path, PurePosixPath

from ingestion.domain.enums.source_provider import SourceProvider
from ingestion.domain.models.repository_clone import SaveTarget

MARKER_ROOT = "_saved"
_MARKER_SUFFIX = ".json"
_REPOSITORY_SEGMENTS = {SourceProvider.GITHUB: 1, SourceProvider.AZURE_DEVOPS: 2}


def folder_prefix(target: SaveTarget) -> str:
    return f"{target.provider}/{target.owner}/{target.repository}/{target.branch}/"


def marker_prefix(provider: SourceProvider) -> str:
    return f"{MARKER_ROOT}/{provider}/"


def marker_key(target: SaveTarget) -> str:
    return (
        f"{marker_prefix(target.provider)}{target.owner}/{target.repository}/"
        f"{target.branch}{_MARKER_SUFFIX}"
    )


def legacy_zip_name(target: SaveTarget) -> str:
    """Saves made before folders were stored as one zip with this name in the folder."""
    return f"{target.repository_name}.zip"


def target_from_marker_key(provider: SourceProvider, key: str) -> SaveTarget | None:
    """Reverse of marker_key; None for objects that are not markers."""
    prefix = marker_prefix(provider)
    if not key.startswith(prefix) or not key.endswith(_MARKER_SUFFIX):
        return None
    parts = key[len(prefix) : -len(_MARKER_SUFFIX)].split("/")
    repo_segments = _REPOSITORY_SEGMENTS[provider]
    if len(parts) < repo_segments + 2 or not all(parts):
        return None
    repository = "/".join(parts[1 : 1 + repo_segments])
    branch = "/".join(parts[1 + repo_segments :])
    return SaveTarget(provider, parts[0], repository, branch)


def safe_relative_path(name: str) -> PurePosixPath | None:
    """A manifest entry as a relative path; None if it could escape its folder."""
    path = PurePosixPath(name)
    if not name or path.is_absolute() or "\\" in name or ".." in path.parts:
        return None
    return path


def local_path(root: Path, name: str) -> Path | None:
    """Where a manifest entry lands under root; None if it would land outside it."""
    relative = safe_relative_path(name)
    if relative is None:
        return None
    destination = root.joinpath(*relative.parts)
    return destination if destination.resolve().is_relative_to(root.resolve()) else None
