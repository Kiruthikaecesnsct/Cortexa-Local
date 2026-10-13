namespace Collector.Application.Remote.Auth;

public enum RemoteAuthMethod
{
    Pat,
    GitHubDevice,
    GitHubApp,
}

public sealed record RemoteAuthAvailability(bool DeviceEnabled, bool AppEnabled)
{
    public static RemoteAuthAvailability None { get; } = new(false, false);
}
