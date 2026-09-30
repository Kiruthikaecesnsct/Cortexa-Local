class GitHubScanError(Exception):
    code = "github_scan_error"


class InvalidScanTargetError(GitHubScanError):
    code = "invalid_scan_target"


class GitHubAuthError(GitHubScanError):
    code = "github_auth_failed"


class GitHubAccessDeniedError(GitHubScanError):
    code = "github_access_denied"


class GitHubNotFoundError(GitHubScanError):
    code = "github_not_found"


class GitHubRateLimitError(GitHubScanError):
    code = "github_rate_limited"


class GitHubEmptyRepositoryError(GitHubScanError):
    code = "github_empty_repository"


class GitHubUpstreamError(GitHubScanError):
    code = "github_upstream_error"


class RepositoryTooLargeError(GitHubScanError):
    code = "repository_too_large"


class MissingUserContextError(GitHubScanError):
    code = "missing_user_context"


class CloneNotFoundError(GitHubScanError):
    code = "clone_not_found"


class CloneStorageUnavailableError(GitHubScanError):
    code = "clone_storage_unavailable"


class CloneExecutionError(GitHubScanError):
    code = "clone_failed"
