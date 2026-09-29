import re

from ingestion.domain.enums.git_host import GitHost
from ingestion.domain.errors.clone_errors import InvalidRepoUrlError, UnsupportedHostError
from ingestion.domain.models.clone_request import RepoRef

_GITHUB_HTTPS = re.compile(r"^https://github\.com/(?P<owner>[^/]+)/(?P<repo>[^/]+?)(?:\.git)?$")
_GITHUB_SSH = re.compile(r"^git@github\.com:(?P<owner>[^/]+)/(?P<repo>[^/]+?)(?:\.git)?$")
_AZDO_HTTPS = re.compile(
    r"^https://dev\.azure\.com/(?P<org>[^/]+)/(?P<project>[^/]+)/_git/(?P<repo>[^/]+?)(?:\.git)?$"
)
_AZDO_SSH = re.compile(
    r"^git@ssh\.dev\.azure\.com:v3/(?P<org>[^/]+)/(?P<project>[^/]+)/(?P<repo>[^/]+?)(?:\.git)?$"
)

_KNOWN_HOSTS = {"github.com", "dev.azure.com", "ssh.dev.azure.com"}


def parse(raw_url: str) -> RepoRef:
    _guard_known_host(raw_url)
    return _match_patterns(raw_url)


def _guard_known_host(raw_url: str) -> None:
    lowered = raw_url.lower()
    has_known = any(host in lowered for host in _KNOWN_HOSTS)
    if not has_known:
        raise UnsupportedHostError(f"Host not supported in URL: {raw_url!r}")


def _match_patterns(raw_url: str) -> RepoRef:
    ref = _try_github_https(raw_url)
    if ref:
        return ref
    ref = _try_github_ssh(raw_url)
    if ref:
        return ref
    ref = _try_azdo_https(raw_url)
    if ref:
        return ref
    ref = _try_azdo_ssh(raw_url)
    if ref:
        return ref
    raise InvalidRepoUrlError(f"URL did not match any supported pattern: {raw_url!r}")


def _try_github_https(raw_url: str) -> RepoRef | None:
    m = _GITHUB_HTTPS.match(raw_url)
    if not m:
        return None
    owner, repo = m.group("owner"), m.group("repo")
    return RepoRef(
        host=GitHost.GITHUB,
        owner=owner,
        repo=repo,
        normalized_https_url=f"https://github.com/{owner}/{repo}.git",
        use_ssh=False,
    )


def _try_github_ssh(raw_url: str) -> RepoRef | None:
    m = _GITHUB_SSH.match(raw_url)
    if not m:
        return None
    owner, repo = m.group("owner"), m.group("repo")
    return RepoRef(
        host=GitHost.GITHUB,
        owner=owner,
        repo=repo,
        normalized_https_url=f"https://github.com/{owner}/{repo}.git",
        use_ssh=True,
    )


def _try_azdo_https(raw_url: str) -> RepoRef | None:
    m = _AZDO_HTTPS.match(raw_url)
    if not m:
        return None
    org, project, repo = m.group("org"), m.group("project"), m.group("repo")
    return RepoRef(
        host=GitHost.AZURE_DEVOPS,
        owner=org,
        repo=f"{project}/{repo}",
        normalized_https_url=f"https://dev.azure.com/{org}/{project}/_git/{repo}",
        use_ssh=False,
    )


def _try_azdo_ssh(raw_url: str) -> RepoRef | None:
    m = _AZDO_SSH.match(raw_url)
    if not m:
        return None
    org, project, repo = m.group("org"), m.group("project"), m.group("repo")
    return RepoRef(
        host=GitHost.AZURE_DEVOPS,
        owner=org,
        repo=f"{project}/{repo}",
        normalized_https_url=f"https://dev.azure.com/{org}/{project}/_git/{repo}",
        use_ssh=True,
    )
