namespace Collector.Server.Infrastructure.Identity;

public sealed class IdentityOptions
{
    public const string SectionName = "Identity";

    public string Issuer { get; set; } = string.Empty;

    public string Audience { get; set; } = string.Empty;

    // Must be copied verbatim from the same source the native identity/api-gateway run uses
    // for its JWT signing key. Never invent a second source of truth for this value - a
    // mismatch here causes every request to fail authentication silently with a plain 401
    // that the UI reports as "session expired" even though the caller's session is valid.
    // See US146 for the production incident this caused.
    public string SigningKey { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = string.Empty;

    // Must be copied verbatim from the same source the native identity/api-gateway run uses
    // for the X-Internal-Key value. Never invent a second source of truth for this value.
    // See US146.
    public string InternalKey { get; set; } = string.Empty;

    public string InternalKeyHeaderName { get; set; } = "X-Internal-Key";

    public int StatusCacheSeconds { get; set; } = 5;

    public int ClockSkewSeconds { get; set; } = 30;

    public int StatusTimeoutSeconds { get; set; } = 5;
}
