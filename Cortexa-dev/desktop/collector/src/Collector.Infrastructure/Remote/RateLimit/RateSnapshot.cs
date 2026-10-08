namespace Collector.Infrastructure.Remote.RateLimit;

public sealed record RateSnapshot(int? Remaining, DateTimeOffset? ResetAt, TimeSpan? RetryAfter)
{
    public static RateSnapshot Empty { get; } = new(null, null, null);
}
