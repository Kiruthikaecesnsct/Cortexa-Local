from enum import StrEnum


class SourceProvider(StrEnum):
    """Where a saved repository came from; also the top folder of its stored zip."""

    GITHUB = "github"
    AZURE_DEVOPS = "azure-devops"
