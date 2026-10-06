namespace Collector.Server.Infrastructure.Identity;

public sealed class IdentityOptions
{
    public const string SectionName = "Identity";

    public string Issuer { get; set; } = string.Empty;

    public string Audience { get; set; } = string.Empty;

    public string SigningKey { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = string.Empty;

    public string InternalKey { get; set; } = string.Empty;

    public string InternalKeyHeaderName { get; set; } = "X-Internal-Key";

    public int StatusCacheSeconds { get; set; } = 5;

    public int ClockSkewSeconds { get; set; } = 30;

    public int StatusTimeoutSeconds { get; set; } = 5;
}
