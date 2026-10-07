using System.Text.RegularExpressions;

namespace Collector.Application.Extraction;

public sealed partial class HeadingDetector
{
    private static readonly HashSet<string> ArticlesAndPrepositions = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "and", "or", "but", "of", "to", "in", "on", "at", "for", "with", "by", "from",
    };

    private const int MinAllCapsLength = 4;
    private const int MaxAllCapsLength = 100;
    private const int MinTitleLength = 10;
    private const int MaxTitleLength = 80;
    private const int MinTitleWords = 3;
    private const int MaxTitleWords = 12;
    private const double MinCapitalizedRatio = 0.7;
    private const string SentenceEndings = ".?!";

    // Ordered rules; the first one that returns heading text wins.
    private static readonly Func<string, string?, string?>[] HeadingRules =
    [
        (line, _) => AtxHeading().IsMatch(line) ? ExtractAtxText(line) : null,
        (line, nextLine) => IsMarkdownSetextHeading(line, nextLine) ? line.Trim() : null,
        (line, _) => NumberedSection().IsMatch(line) ? line.Trim() : null,
        (line, _) => IsAllCapsSection(line) ? line.Trim() : null,
        (line, nextLine) => IsTitleCaseHeading(line, nextLine) ? line.Trim() : null,
    ];

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
        foreach (var rule in HeadingRules)
        {
            var text = rule(line, nextLine);
            if (text is not null)
            {
                return (true, text);
            }
        }

        return (false, string.Empty);
    }

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

    private static bool IsAllCapsSection(string line)
    {
        var stripped = line.Trim();
        if (stripped.Length is < MinAllCapsLength or > MaxAllCapsLength)
        {
            return false;
        }

        var alphaWords = SplitWords(stripped).Where(w => w.Any(char.IsLetter)).ToArray();
        return alphaWords.Length > 0 && alphaWords.All(IsUpperCaseWord);
    }

    private static bool IsUpperCaseWord(string word) =>
        word == word.ToUpperInvariant() && word != word.ToLowerInvariant();

    private static bool IsTitleCaseHeading(string line, string? nextLine)
    {
        var stripped = line.Trim();
        return stripped.Length is >= MinTitleLength and <= MaxTitleLength
            && !EndsWithSentencePunctuation(stripped)
            && HasTitleCaseWords(stripped)
            && NextLineStartsUpper(nextLine);
    }

    private static bool EndsWithSentencePunctuation(string text) =>
        text.Length > 0 && SentenceEndings.Contains(text[^1]);

    private static bool HasTitleCaseWords(string text)
    {
        var words = SplitWords(text).Where(w => w.All(char.IsLetter)).ToArray();
        if (words.Length is < MinTitleWords or > MaxTitleWords)
        {
            return false;
        }

        return CapitalizedRatio(words) >= MinCapitalizedRatio;
    }

    private static double CapitalizedRatio(string[] words)
    {
        var significantWords = words
            .Where(w => !ArticlesAndPrepositions.Contains(w) || w.Length > 3)
            .ToArray();
        if (significantWords.Length == 0)
        {
            return 0;
        }

        var capitalizedCount = significantWords.Count(w => char.IsUpper(w[0]));
        return (double)capitalizedCount / significantWords.Length;
    }

    private static bool NextLineStartsUpper(string? nextLine)
    {
        var nextTrimmed = nextLine?.Trim();
        return string.IsNullOrEmpty(nextTrimmed) || char.IsUpper(nextTrimmed[0]);
    }

    private static string[] SplitWords(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

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
