namespace Collector.Infrastructure.Remote;

internal static class RemotePathRules
{
    private static readonly char[] Separators = ['/', '\\'];

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public static string[]? SafeSegments(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || Separators.Contains(path[0]))
        {
            return null;
        }

        var segments = path.Split(Separators);
        return segments.All(IsSafeSegment) ? segments : null;
    }

    private static bool IsSafeSegment(string segment)
    {
        if (segment.Length == 0 || segment is "." or ".." || segment.EndsWith('.') || segment.EndsWith(' '))
        {
            return false;
        }

        if (segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return false;
        }

        var stem = segment.Split('.')[0].TrimEnd(' ');
        return !ReservedNames.Contains(stem);
    }
}
