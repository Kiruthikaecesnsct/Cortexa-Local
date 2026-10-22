using System.Globalization;
using System.Text.RegularExpressions;
using Collector.Application.Ports;

namespace Collector.Infrastructure.Ai;

public static partial class GeminiQuotaHintParser
{
    public static AiQuotaHint? Parse(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        var retryAfter = ParseRetryAfter(message);
        var scope = ParseScope(message);
        return retryAfter is null && scope == AiQuotaScope.Unknown ? null : new AiQuotaHint(retryAfter, scope);
    }

    private static TimeSpan? ParseRetryAfter(string message)
    {
        var match = RetryDelay().Match(message);
        var seconds = match.Success ? match.Groups["seconds"].Value : null;
        return double.TryParse(seconds, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value >= 0
            ? TimeSpan.FromSeconds(value)
            : null;
    }

    private static AiQuotaScope ParseScope(string message)
    {
        if (message.Contains("PerDay", StringComparison.OrdinalIgnoreCase))
        {
            return AiQuotaScope.PerDay;
        }

        return message.Contains("PerMinute", StringComparison.OrdinalIgnoreCase) ? AiQuotaScope.PerMinute : AiQuotaScope.Unknown;
    }

    [GeneratedRegex(@"(?:retry\s+(?:in|after)\s+|retryDelay\W+)(?<seconds>\d+(?:\.\d+)?)\s*s", RegexOptions.IgnoreCase, 250)]
    private static partial Regex RetryDelay();
}
