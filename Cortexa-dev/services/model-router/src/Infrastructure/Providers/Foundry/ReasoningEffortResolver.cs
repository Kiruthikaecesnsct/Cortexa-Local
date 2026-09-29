namespace Cortexa.ModelRouter.Infrastructure.Providers.Foundry;

public static class ReasoningEffortResolver
{
    private static readonly string[] EffortRanking =
    [
        "none",
        "minimal",
        "low",
        "medium",
        "high",
        "xhigh"
    ];

    public static string? Resolve(string? requestedEffort, List<string>? supportedEfforts)
    {
        if (string.IsNullOrWhiteSpace(requestedEffort))
            return null;

        if (supportedEfforts is null || supportedEfforts.Count == 0)
            return requestedEffort;

        foreach (var supported in supportedEfforts)
        {
            if (string.Equals(requestedEffort, supported, StringComparison.OrdinalIgnoreCase))
                return requestedEffort;
        }

        return CoerceToNearest(requestedEffort, supportedEfforts);
    }

    private static string CoerceToNearest(string requestedEffort, List<string> supportedEfforts)
    {
        var requestedIndex = Array.IndexOf(EffortRanking, requestedEffort.ToLowerInvariant());
        if (requestedIndex < 0)
            return supportedEfforts[0];

        string? nearestEffort = null;
        var smallestDistance = int.MaxValue;

        foreach (var supported in supportedEfforts)
        {
            var supportedIndex = Array.IndexOf(EffortRanking, supported.ToLowerInvariant());
            if (supportedIndex < 0)
                continue;

            var distance = Math.Abs(requestedIndex - supportedIndex);
            if (IsCloserOrRoundsUp(distance, smallestDistance, supportedIndex, requestedIndex))
            {
                smallestDistance = distance;
                nearestEffort = supported;
            }
        }

        return nearestEffort ?? supportedEfforts[0];
    }

    private static bool IsCloserOrRoundsUp(int distance, int smallestDistance, int supportedIndex, int requestedIndex)
    {
        if (distance < smallestDistance)
            return true;

        return distance == smallestDistance && supportedIndex > requestedIndex;
    }
}
