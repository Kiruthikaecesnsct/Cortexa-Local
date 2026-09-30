import re
from urllib.parse import urlsplit

from ingestion.domain.errors.azure_devops_scan_errors import InvalidScanTargetError

_AZURE_DEVOPS_HOSTS = {"dev.azure.com"}
# Azure DevOps organization names: alphanumeric or single hyphens, 1-50 chars, no
# leading/trailing hyphen.
_ORG = re.compile(r"^[A-Za-z0-9](?:[A-Za-z0-9-]{0,48}[A-Za-z0-9])?$")
# Project and repository names may not contain a path separator; length capped to
# Azure DevOps' 64-char project name limit.
_SEGMENT = re.compile(r"^[^/\\]{1,64}$")
_BRANCH = re.compile(r"^[A-Za-z0-9._/-]{1,255}$")


def parse_organization(org_url: str) -> str:
    parts = urlsplit(org_url.strip())
    if parts.scheme != "https" or (parts.hostname or "").lower() not in _AZURE_DEVOPS_HOSTS:
        raise InvalidScanTargetError("Organization URL must be an https://dev.azure.com/ URL.")
    segments = [s for s in parts.path.split("/") if s]
    if len(segments) != 1 or not _ORG.match(segments[0]):
        raise InvalidScanTargetError(
            "Organization URL must look like https://dev.azure.com/{organization}."
        )
    return segments[0]


def _valid_segment(value: str) -> bool:
    return bool(_SEGMENT.match(value)) and value not in {".", ".."} and value.strip() == value


def validate_repository(repository: str) -> str:
    parts = repository.split("/")
    if len(parts) != 2 or not all(_valid_segment(p) for p in parts):
        raise InvalidScanTargetError("Repository must be given as '{project}/{repository}'.")
    return repository


def split_repository(repository: str) -> tuple[str, str]:
    project, repo = validate_repository(repository).split("/")
    return project, repo


def validate_branch(branch: str) -> str:
    invalid = (
        not _BRANCH.match(branch)
        or ".." in branch
        or "//" in branch
        or branch.startswith("/")
        or branch.endswith(("/", ".lock"))
    )
    if invalid:
        raise InvalidScanTargetError("Branch name is not valid.")
    return branch


def validate_organization(organization: str) -> str:
    if not _ORG.match(organization):
        raise InvalidScanTargetError("Organization name is not valid.")
    return organization
