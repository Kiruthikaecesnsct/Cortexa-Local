namespace Collector.Infrastructure.Remote;

internal static class RemoteUrl
{
    public static string Segment(string value) => Uri.EscapeDataString(value);

    public static string Path(string value) =>
        string.Join('/', value.Split('/').Select(Uri.EscapeDataString));

    public static Uri BaseAddress(string baseUrl) => new($"{baseUrl.TrimEnd('/')}/", UriKind.Absolute);

    public static Uri Relative(string value) => new(value, UriKind.Relative);
}
