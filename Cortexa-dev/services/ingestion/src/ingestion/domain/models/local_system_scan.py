from datetime import datetime
from typing import Literal

from pydantic import BaseModel

DirectoryEntryType = Literal["file", "directory", "symlink"]


class DirectoryEntry(BaseModel):
    name: str
    path: str
    type: DirectoryEntryType
    size: int | None = None
    modified_at: datetime | None = None


class DirectoryListing(BaseModel):
    # Absolute path as resolved by the remote host, not necessarily the raw
    # path the caller requested (e.g. "~" resolves to the user's home dir).
    path: str
    entries: list[DirectoryEntry]
    truncated: bool = False
