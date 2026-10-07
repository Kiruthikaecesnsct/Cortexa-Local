using System.Text;
using Collector.Domain.Enums;
using Collector.Domain.Extraction;

namespace Collector.Application.Knowledge;

public sealed record Anchor(int Offset, int Length, int? LineStart, int? LineEnd);

public sealed class AnchorLocator
{
    private const char ZeroWidthSpace = '​';

    private static readonly (string Neutralized, string Original)[] Substitutions =
    [
        ("- -", "--"),
        ("= =", "=="),
    ];

    public Anchor? Locate(ExtractionUnit unit, string? quote)
    {
        var cleaned = quote?.Replace(ZeroWidthSpace.ToString(), string.Empty, StringComparison.Ordinal).Trim();
        if (string.IsNullOrEmpty(cleaned))
        {
            return null;
        }

        var hit = Candidates(cleaned).Select(candidate => Find(unit.Text, candidate)).FirstOrDefault(found => found is not null);
        return hit is null ? null : ToAnchor(unit, hit.Value.Offset, hit.Value.Length);
    }

    private static IEnumerable<string> Candidates(string cleaned)
    {
        yield return cleaned;
        var undone = Substitutions.Aggregate(
            cleaned,
            (current, pair) => current.Replace(pair.Neutralized, pair.Original, StringComparison.Ordinal));
        if (!string.Equals(undone, cleaned, StringComparison.Ordinal))
        {
            yield return undone;
        }
    }

    private static (int Offset, int Length)? Find(string text, string candidate)
    {
        var exact = text.IndexOf(candidate, StringComparison.Ordinal);
        return exact >= 0 ? (exact, candidate.Length) : FindCollapsed(text, candidate);
    }

    private static (int Offset, int Length)? FindCollapsed(string text, string candidate)
    {
        var collapsedQuote = string.Join(' ', candidate.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (collapsedQuote.Length == 0)
        {
            return null;
        }

        var (collapsed, map) = Collapse(text);
        var index = collapsed.IndexOf(collapsedQuote, StringComparison.Ordinal);
        if (index < 0)
        {
            return null;
        }

        var start = map[index];
        var end = map[index + collapsedQuote.Length - 1];
        return (start, end - start + 1);
    }

    private static (string Text, List<int> Map) Collapse(string text)
    {
        var builder = new StringBuilder(text.Length);
        var map = new List<int>(text.Length);
        var previousWasSpace = false;
        for (var index = 0; index < text.Length; index++)
        {
            var isSpace = char.IsWhiteSpace(text[index]);
            if (isSpace && previousWasSpace)
            {
                continue;
            }

            builder.Append(isSpace ? ' ' : text[index]);
            map.Add(index);
            previousWasSpace = isSpace;
        }

        return (builder.ToString(), map);
    }

    private static Anchor? ToAnchor(ExtractionUnit unit, int offset, int length)
    {
        if (unit.UnitKind != UnitKind.File || unit.StartLine is null || unit.EndLine is null)
        {
            return new Anchor(offset, length, null, null);
        }

        var lineStart = unit.StartLine.Value + CountLineBreaks(unit.Text, 0, offset);
        var lineEnd = lineStart + CountLineBreaks(unit.Text, offset, offset + length);
        var inside = lineStart >= unit.StartLine && lineEnd <= unit.EndLine;
        return inside ? new Anchor(offset, length, lineStart, lineEnd) : null;
    }

    private static int CountLineBreaks(string text, int from, int to) =>
        text.AsSpan(from, to - from).Count('\n');
}
