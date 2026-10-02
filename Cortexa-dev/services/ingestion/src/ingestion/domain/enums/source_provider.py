from enum import StrEnum


class SourceProvider(StrEnum):
    """Where a saved repository came from; also the top folder it is stored under."""

    GITHUB = "github"
    AZURE_DEVOPS = "azure-devops"
