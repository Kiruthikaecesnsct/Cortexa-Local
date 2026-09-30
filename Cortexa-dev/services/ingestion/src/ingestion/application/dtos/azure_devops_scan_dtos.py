from pydantic import BaseModel, Field, SecretStr

from ingestion.domain.models.azure_devops_scan import BranchSummary, RepositorySummary

_MAX_URL_LENGTH = 200
_MAX_PAT_LENGTH = 255


class ListRepositoriesRequest(BaseModel):
    org_url: str = Field(min_length=1, max_length=_MAX_URL_LENGTH)
    pat: SecretStr = Field(min_length=1, max_length=_MAX_PAT_LENGTH)


class ListBranchesRequest(ListRepositoriesRequest):
    # "{project}/{repository}", the handle returned as RepositorySummary.name.
    repository: str = Field(min_length=1, max_length=129)


class RepositoryTreeRequest(ListBranchesRequest):
    branch: str = Field(min_length=1, max_length=255)


class ListRepositoriesResponse(BaseModel):
    # Named "owner" (not "organization") so the shape matches the frontend's shared
    # ListRepositoriesDto, letting the Azure DevOps wizard reuse the GitHub scan UI.
    owner: str
    repositories: list[RepositorySummary]


class ListBranchesResponse(BaseModel):
    repository: str
    branches: list[BranchSummary]
