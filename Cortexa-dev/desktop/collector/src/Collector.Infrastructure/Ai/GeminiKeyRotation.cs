using Collector.Application.Secrets;

namespace Collector.Infrastructure.Ai;

public static class GeminiKeyRotation
{
    public static IReadOnlyList<GeminiKey> OrderFrom(IReadOnlyList<GeminiKey> keys, string? activeKeyId)
    {
        if (keys.Count == 0 || activeKeyId is null)
        {
            return keys;
        }

        var startIndex = IndexOf(keys, activeKeyId);
        return startIndex <= 0 ? keys : [.. keys.Skip(startIndex), .. keys.Take(startIndex)];
    }

    private static int IndexOf(IReadOnlyList<GeminiKey> keys, string activeKeyId)
    {
        for (var i = 0; i < keys.Count; i++)
        {
            if (string.Equals(keys[i].Id, activeKeyId, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return 0;
    }
}
