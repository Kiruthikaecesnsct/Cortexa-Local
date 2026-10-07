using System.Text.RegularExpressions;

namespace Collector.Application.Extraction;

public sealed partial class HeadingDetector
{
    private static readonly HashSet<string> ArticlesAndPrepositions = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "and", "or", "but", "of", "to", "in", "on", "at", "for", "with", "by", "from",
    };

    public IReadOnlyList<Heading> DetectHeadings(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var lines = text.Split('\n');
        var headings = new List<Heading>();
        var currentOffset = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var nextLine = i + 1 < lines.Length ? lines[i + 1] : null;
            var (isHeading, headingText) = ClassifyHeading(line, nextLine);
            if (isHeading && !string.IsNullOrEmpty(headingText))
            {
                headings.Add(new Heading
                {
                    Text = headingText,
                    StartChar = currentOffset,
                    EndChar = currentOffset + line.Length,
                });
            }

            currentOffset += line.Length + 1;
        }

        return headings;
    }

    public string? FindHeadingForPosition(IReadOnlyList<Heading> headings, int position)
    {
        if (headings.Count == 0)
        {
            return null;
        }

        Heading? nearest = null;
        foreach (var heading in headings)
        {
            if (heading.StartChar <= position)
            {
                nearest = heading;
            }
            else
            {
                break;
            }
        }

        return nearest?.Text;
    }

    private static (bool IsHeading, string Text) ClassifyHeading(string line, string? nextLine)
    {
        if (IsMarkdownAtxHeading(line))
        {
            return (true, ExtractAtxText(line));
        }

        if (IsMarkdownSetextHeading(line, nextLine))
        {
            return (true, line.Trim());
        }

        if (IsNumberedSection(line))
        {
            return (true, line.Trim());
        }

        if (IsAllCapsSection(line))
        {
            return (true, line.Trim());
        }

        if (IsTitleCaseHeading(line, nextLine))
        {
            return (true, line.Trim());
        }

        return (false, string.Empty);
    }

    private static bool IsMarkdownAtxHeading(string line) => AtxHeading().IsMatch(line);

    private static bool IsMarkdownSetextHeading(string line, string? nextLine)
    {
        if (string.IsNullOrEmpty(nextLine))
        {
            return false;
        }

        var isUnderline = SetextUnderline().IsMatch(nextLine.Trim());
        var trimmed = line.Trim();
        var hasContent = trimmed.Length > 0 && trimmed.Length < 100;
        return isUnderline && hasContent;
    }

    private static bool IsNumberedSection(string line) => NumberedSection().IsMatch(line);

    private static bool IsAllCapsSection(string line)
    {
        var stripped = line.Trim();
        if (stripped.Length < 4 || stripped.Length > 100)
        {
            return false;
        }

        var words = stripped.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return false;
        }

        var alphaWords = words.Where(w => w.Any(char.IsLetter)).ToArray();
        if (alphaWords.Length == 0)
        {
            return false;
        }

        var allUpper = alphaWords.All(w => w == w.ToUpperInvariant() && w != w.ToLowerInvariant());
        var hasAlpha = stripped.Any(char.IsLetter);
        return allUpper && hasAlpha;
    }

    private static bool IsTitleCaseHeading(string line, string? nextLine)
    {
        var stripped = line.Trim();
        if (stripped.Length < 10 || stripped.Length > 80)
        {
            return false;
        }

        if (stripped.EndsWith('.') || stripped.EndsWith('?') || stripped.EndsWith('!'))
        {
            return false;
        }

        var words = stripped.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 0 && w.All(char.IsLetter))
            .ToArray();
        if (words.Length < 3 || words.Length > 12)
        {
            return false;
        }

        var significantWords = words
            .Where(w => !ArticlesAndPrepositions.Contains(w) || w.Length > 3)
            .ToArray();
        if (significantWords.Length == 0)
        {
            return false;
        }

        var capitalizedCount = significantWords.Count(w => char.IsUpper(w[0]));
        var ratio = (double)capitalizedCount / significantWords.Length;
        if (ratio < 0.7)
        {
            return false;
        }

        var nextTrimmed = nextLine?.Trim();
        if (!string.IsNullOrEmpty(nextTrimmed) && !char.IsUpper(nextTrimmed[0]))
        {
            return false;
        }

        return true;
    }

    private static string ExtractAtxText(string line) => AtxPrefix().Replace(line, string.Empty).Trim();

    [GeneratedRegex(@"^#{1,6}\s+\S")]
    private static partial Regex AtxHeading();

    [GeneratedRegex(@"^#{1,6}\s+")]
    private static partial Regex AtxPrefix();

    [GeneratedRegex(@"^[=\-]{3,}$")]
    private static partial Regex SetextUnderline();

    [GeneratedRegex(@"^(?:\d+\.)+\d*\s+[A-Z]")]
    private static partial Regex NumberedSection();
}
