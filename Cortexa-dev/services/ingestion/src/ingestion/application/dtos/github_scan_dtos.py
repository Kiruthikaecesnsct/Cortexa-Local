from pydantic import BaseModel, Field, SecretStr

from ingestion.domain.models.github_scan import BranchSummary, RepositorySummary
from ingestion.domain.models.repository_clone import RepositoryClone

_MAX_URL_LENGTH = 200
_MAX_PAT_LENGTH = 255


class ListRepositoriesRequest(BaseModel):
    org_url: str = Field(min_length=1, max_length=_MAX_URL_LENGTH)
    pat: SecretStr = Field(min_length=1, max_length=_MAX_PAT_LENGTH)


class ListBranchesRequest(ListRepositoriesRequest):
    repository: str = Field(min_length=1, max_length=100)


class RepositoryTreeRequest(ListBranchesRequest):
    branch: str = Field(min_length=1, max_length=255)


class ListRepositoriesResponse(BaseModel):
    owner: str
    repositories: list[RepositorySummary]


class CloneListResponse(BaseModel):
    clones: list[RepositoryClone]


class CloneFilesResponse(BaseModel):
    files: list[str]


class ListBranchesResponse(BaseModel):
    repository: str
    branches: list[BranchSummary]
