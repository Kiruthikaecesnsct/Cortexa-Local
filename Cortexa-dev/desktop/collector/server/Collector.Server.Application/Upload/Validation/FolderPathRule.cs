namespace Collector.Server.Application.Upload.Validation;

public static class FolderPathRule
{
    private const char Separator = '/';
    private const char AltSeparator = '\\';
    private const char DriveMarker = ':';
    private const string CurrentSegment = ".";
    private const string ParentSegment = "..";

    public static bool IsFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || HasUnsafeCharacters(path))
        {
            return false;
        }

        var normalized = path.Replace(AltSeparator, Separator);
        return normalized[^1] == Separator && IsRelative(normalized) && HasSafeSegments(normalized);
    }

    private static bool HasUnsafeCharacters(string path) =>
        path != path.Trim() || path.Any(char.IsControl);

    private static bool IsRelative(string normalized) =>
        normalized[0] != Separator && !normalized.Contains(DriveMarker);

    private static bool HasSafeSegments(string normalized) =>
        normalized[..^1]
            .Split(Separator)
            .All(segment => segment.Length > 0 && segment != CurrentSegment && segment != ParentSegment);
}
