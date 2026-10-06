using System.Globalization;
using System.Text;

namespace Collector.Server.Application.Upload;

public static class FilenameSanitizer
{
    private static readonly HashSet<int> QuoteCodePoints =
    [
        '"',
        '\'',
        '`',
        '‘',
        '’',
        '“',
        '”'
    ];

    public static bool TryClean(string? filename, out string cleaned)
    {
        cleaned = Clean(filename);
        return cleaned.Length > 0;
    }

    public static string Clean(string? filename)
    {
        if (string.IsNullOrEmpty(filename))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(Math.Min(filename.Length, UploadLimits.MaxFilenameLength));
        foreach (var rune in filename.EnumerateRunes())
        {
            if (IsAllowed(rune) && !TryAppend(builder, rune))
            {
                break;
            }
        }

        return builder.ToString().Trim();
    }

    private static bool IsAllowed(Rune rune) =>
        !QuoteCodePoints.Contains(rune.Value) && !IsForbiddenCategory(Rune.GetUnicodeCategory(rune));

    private static bool IsForbiddenCategory(UnicodeCategory category) =>
        category is UnicodeCategory.Control
            or UnicodeCategory.Format
            or UnicodeCategory.LineSeparator
            or UnicodeCategory.ParagraphSeparator;

    private static bool TryAppend(StringBuilder builder, Rune rune)
    {
        if (builder.Length + rune.Utf16SequenceLength > UploadLimits.MaxFilenameLength)
        {
            return false;
        }

        builder.Append(rune.ToString());
        return true;
    }
}
