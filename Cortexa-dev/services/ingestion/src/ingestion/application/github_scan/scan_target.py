import re
from urllib.parse import urlsplit

from ingestion.domain.errors.github_scan_errors import (
    InvalidScanTargetError,
    MissingUserContextError,
)

_GITHUB_HOSTS = {"github.com", "www.github.com"}
# GitHub account names: alphanumeric or single hyphens, max 39 chars, no leading hyphen.
_OWNER = re.compile(r"^[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})$")
_REPO = re.compile(r"^[A-Za-z0-9._-]{1,100}$")
_BRANCH = re.compile(r"^[A-Za-z0-9._/-]{1,255}$")


def parse_owner(org_url: str) -> str:
    parts = urlsplit(org_url.strip())
    if parts.scheme != "https" or (parts.hostname or "").lower() not in _GITHUB_HOSTS:
        raise InvalidScanTargetError("Organization URL must be an https://github.com/ URL.")
    segments = [s for s in parts.path.split("/") if s]
    if segments[:1] == ["orgs"]:
        segments = segments[1:]
    if len(segments) != 1 or not _OWNER.match(segments[0]):
        raise InvalidScanTargetError(
            "Organization URL must look like https://github.com/{organization}."
        )
    return segments[0]


def validate_repository(repo: str) -> str:
    if not _REPO.match(repo) or repo in {".", ".."}:
        raise InvalidScanTargetError("Repository name is not valid.")
    return repo


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


_USER_ID = re.compile(r"^[A-Za-z0-9-]{1,64}$")


def validate_user_id(user_id: str | None) -> str:
    if not user_id or not _USER_ID.match(user_id):
        raise MissingUserContextError("Sign in to save repositories.")
    return user_id


def validate_owner(owner: str) -> str:
    if not _OWNER.match(owner):
        raise InvalidScanTargetError("Owner name is not valid.")
    return owner
