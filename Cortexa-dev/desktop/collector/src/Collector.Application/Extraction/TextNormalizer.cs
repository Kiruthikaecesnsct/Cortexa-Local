using System.Text;
using System.Text.RegularExpressions;

namespace Collector.Application.Extraction;

public sealed partial class TextNormalizer
{
    public string Normalize(string raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return string.Empty;
        }

        var unified = raw.Replace("\r\n", "\n").Replace("\r", "\n");
        var lines = unified.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            lines[i] = InlineWhitespace().Replace(lines[i], " ").TrimEnd();
        }

        var collapsed = CollapseBlankLines().Replace(string.Join('\n', lines), "\n\n");
        var trimmed = collapsed.Trim();
        return EnsureUtf8(trimmed);
    }

    private static string EnsureUtf8(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        return Encoding.UTF8.GetString(bytes);
    }

    [GeneratedRegex(@"[ \t]+")]
    private static partial Regex InlineWhitespace();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex CollapseBlankLines();
}
