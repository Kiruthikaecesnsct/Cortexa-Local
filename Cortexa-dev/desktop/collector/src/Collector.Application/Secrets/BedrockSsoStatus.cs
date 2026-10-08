namespace Collector.Application.Secrets;

public sealed record BedrockSsoStatus(bool IsConnected, DateTimeOffset? ExpiresAtUtc)
{
    public static readonly BedrockSsoStatus NotConnected = new(false, null);
}
