from datetime import datetime
from typing import Literal

from pydantic import BaseModel

TreeEntryType = Literal["blob", "tree", "commit"]


class RepositorySummary(BaseModel):
    name: str
    full_name: str
    private: bool
    default_branch: str | None
    html_url: str
    description: str | None = None
    size_kb: int = 0
    updated_at: datetime | None = None


class BranchSummary(BaseModel):
    name: str
    protected: bool = False
    commit_sha: str | None = None


class TreeEntry(BaseModel):
    path: str
    type: TreeEntryType
    size: int | None = None


class RepositoryTree(BaseModel):
    repository: str
    branch: str
    entries: list[TreeEntry]
    truncated: bool = False
