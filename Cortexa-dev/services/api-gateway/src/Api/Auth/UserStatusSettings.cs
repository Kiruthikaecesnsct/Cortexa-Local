namespace Cortexa.ApiGateway.Api.Auth;

public sealed class UserStatusSettings
{
    public const string SectionName = "UserStatus";

    public string IdentityInternalBaseUrl { get; set; } = string.Empty;

    public string InternalKeyHeaderName { get; set; } = "X-Internal-Key";

    public string InternalKeySecretName { get; set; } = "internal-api-key";

    public string InternalKey { get; set; } = string.Empty;

    public int CacheTtlSeconds { get; set; } = 5;

    public int RequestTimeoutSeconds { get; set; } = 3;
}
