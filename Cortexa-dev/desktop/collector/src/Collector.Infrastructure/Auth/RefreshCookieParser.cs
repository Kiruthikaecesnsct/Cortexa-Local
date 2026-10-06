namespace Collector.Infrastructure.Auth;

public static class RefreshCookieParser
{
    public const string CookieName = "refresh_token";

    public static string? TryGetRefreshToken(IEnumerable<string> setCookieHeaders) =>
        setCookieHeaders.Select(ReadValue).FirstOrDefault(value => value is not null);

    private static string? ReadValue(string header)
    {
        var parts = header.Split(';', StringSplitOptions.TrimEntries);
        var separator = parts[0].IndexOf('=', StringComparison.Ordinal);
        if (separator < 0 || !parts[0].AsSpan(0, separator).Trim().Equals(CookieName, StringComparison.Ordinal))
        {
            return null;
        }

        var value = parts[0][(separator + 1)..].Trim();
        return value.Length == 0 || IsExpired(parts.Skip(1)) ? null : value;
    }

    private static bool IsExpired(IEnumerable<string> attributes) =>
        attributes.Any(attribute => attribute.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Equals("Max-Age=0", StringComparison.OrdinalIgnoreCase));
}
