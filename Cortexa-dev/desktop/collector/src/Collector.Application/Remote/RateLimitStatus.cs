using Collector.Domain.Enums;

namespace Collector.Application.Remote;

public sealed record RateLimitStatus(SourceType Provider, bool IsPaused, DateTimeOffset? PausedUntil, int? Remaining)
{
    public static RateLimitStatus Running(SourceType provider, int? remaining = null) =>
        new(provider, false, null, remaining);
}
