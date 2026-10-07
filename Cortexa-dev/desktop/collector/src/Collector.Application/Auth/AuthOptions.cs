namespace Collector.Application.Auth;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public int RefreshSkewSeconds { get; set; } = 60;

    public int MinRefreshDelaySeconds { get; set; } = 5;

    public int HttpTimeoutSeconds { get; set; } = 30;
}
