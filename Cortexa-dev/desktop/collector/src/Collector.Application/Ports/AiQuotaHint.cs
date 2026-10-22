namespace Collector.Application.Ports;

public enum AiQuotaScope
{
    Unknown,
    PerMinute,
    PerDay,
}

public sealed record AiQuotaHint(TimeSpan? RetryAfter, AiQuotaScope Scope);
