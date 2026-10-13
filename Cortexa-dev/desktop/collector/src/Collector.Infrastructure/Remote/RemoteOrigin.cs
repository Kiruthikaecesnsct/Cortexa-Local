namespace Collector.Infrastructure.Remote;

internal static class RemoteOrigin
{
    public static bool IsSame(Uri? uri, Uri baseAddress) =>
        uri is { IsAbsoluteUri: true }
        && uri.Scheme == baseAddress.Scheme
        && string.Equals(uri.Host, baseAddress.Host, StringComparison.OrdinalIgnoreCase)
        && uri.Port == baseAddress.Port;
}
