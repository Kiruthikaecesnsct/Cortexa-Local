using System.Net.Http.Headers;

namespace Collector.Infrastructure.Auth;

public static class RetryAfterParser
{
    public static TimeSpan? Parse(RetryConditionHeaderValue? header, DateTimeOffset now)
    {
        if (header is null)
        {
            return null;
        }

        var wait = header.Delta ?? header.Date - now;
        return wait is { } value ? (value < TimeSpan.Zero ? TimeSpan.Zero : value) : null;
    }
}
