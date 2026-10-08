using System.Globalization;
using System.Net;
using Collector.Infrastructure.Auth;

namespace Collector.Infrastructure.Remote.RateLimit;

public abstract class RateHeaderReaderBase(string remainingHeader, string resetHeader) : IRateHeaderReader
{
    public RateSnapshot Read(HttpResponseMessage response, DateTimeOffset now) => new(
        ReadInt(response, remainingHeader),
        ReadEpoch(response, resetHeader),
        RetryAfterParser.Parse(response.Headers.RetryAfter, now));

    public virtual bool IsRateLimited(HttpResponseMessage response, RateSnapshot snapshot) =>
        response.StatusCode == HttpStatusCode.TooManyRequests;

    private static int? ReadInt(HttpResponseMessage response, string name) =>
        FirstValue(response, name) is { } raw
        && int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static DateTimeOffset? ReadEpoch(HttpResponseMessage response, string name) =>
        FirstValue(response, name) is { } raw
        && long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
        && seconds > 0
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;

    private static string? FirstValue(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;
}
