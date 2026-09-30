from ingestion.domain.errors.scan_errors import ScanError


class AzureDevOpsScanError(ScanError):
    code = "azure_devops_scan_error"


class InvalidScanTargetError(AzureDevOpsScanError):
    code = "invalid_scan_target"


class AzureDevOpsAuthError(AzureDevOpsScanError):
    code = "azure_devops_auth_failed"


class AzureDevOpsAccessDeniedError(AzureDevOpsScanError):
    code = "azure_devops_access_denied"


class AzureDevOpsNotFoundError(AzureDevOpsScanError):
    code = "azure_devops_not_found"


class AzureDevOpsRateLimitError(AzureDevOpsScanError):
    code = "azure_devops_rate_limited"


class AzureDevOpsEmptyRepositoryError(AzureDevOpsScanError):
    code = "azure_devops_empty_repository"


class AzureDevOpsUpstreamError(AzureDevOpsScanError):
    code = "azure_devops_upstream_error"
