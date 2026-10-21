namespace Collector.Application.Remote.Auth;

public enum RemoteAuthMethod
{
    Pat,
    GitHubDevice,
    GitHubApp,
    EntraId,
}

public sealed record RemoteAuthAvailability(bool DeviceEnabled, bool AppEnabled, bool EntraEnabled = false)
{
    public static RemoteAuthAvailability None { get; } = new(false, false);
}
