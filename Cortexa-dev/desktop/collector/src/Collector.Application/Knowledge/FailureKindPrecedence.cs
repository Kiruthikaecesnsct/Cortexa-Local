using Collector.Application.Ports;

namespace Collector.Application.Knowledge;

public static class FailureKindPrecedence
{
    private static readonly Dictionary<AiFailureKind, int> Rank = new()
    {
        [AiFailureKind.Permanent] = 0,
        [AiFailureKind.QuotaExceeded] = 1,
        [AiFailureKind.Transient] = 2,
        [AiFailureKind.MissingApiKey] = 3,
    };

    public static AiFailureKind? Dominant(AiFailureKind? first, AiFailureKind? second)
    {
        if (first is null)
        {
            return second;
        }

        if (second is null)
        {
            return first;
        }

        return Rank[first.Value] <= Rank[second.Value] ? first : second;
    }

    public static AiFailureKind? Dominant(IEnumerable<AiFailureKind?> kinds) =>
        kinds.Aggregate((AiFailureKind?)null, Dominant);
}
