namespace Collector.Server.Application.Upload.Validation;

public static class FolderPathRule
{
    private const char Separator = '/';
    private const char AltSeparator = '\\';
    private const int MinLength = 2;

    public static bool IsFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var normalized = path.Trim().Replace(AltSeparator, Separator);
        return normalized.Length >= MinLength && normalized[^1] == Separator;
    }
}
