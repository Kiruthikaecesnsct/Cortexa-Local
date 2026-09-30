class LocalSystemScanError(Exception):
    code = "local_system_scan_error"


class InvalidScanTargetError(LocalSystemScanError):
    code = "invalid_scan_target"


class LocalSystemAuthError(LocalSystemScanError):
    code = "local_system_auth_failed"


class LocalSystemConnectionError(LocalSystemScanError):
    code = "local_system_connection_failed"


class LocalSystemPathNotFoundError(LocalSystemScanError):
    code = "local_system_path_not_found"


class LocalSystemAccessDeniedError(LocalSystemScanError):
    code = "local_system_access_denied"


class LocalSystemTimeoutError(LocalSystemScanError):
    code = "local_system_timeout"


class LocalSystemUpstreamError(LocalSystemScanError):
    code = "local_system_upstream_error"
