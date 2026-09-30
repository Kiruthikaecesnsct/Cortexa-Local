from datetime import datetime
from typing import Literal

from pydantic import BaseModel

TreeEntryType = Literal["blob", "tree"]


class RepositorySummary(BaseModel):
    # "name" is the "{project}/{repository}" handle used to address this repo in
    # follow-up calls; Azure DevOps repository names are unique only within a project.
    name: str
    full_name: str
    # Azure's List Repositories API does not return project visibility; resolving it
    # would cost one extra call per project, so this scan feature does not surface it.
    private: bool = False
    default_branch: str | None = None
    html_url: str
    description: str | None = None
    size_kb: int = 0
    # Azure's List Repositories API does not return a last-updated timestamp either.
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
