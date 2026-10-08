using Collector.Domain.History;
using Collector.Domain.Knowledge;
using Collector.Presentation.Resources;

namespace Collector.Presentation.ViewModels;

public readonly record struct SourceParts(string? FilePath, int? PageNumber, string? Section, int? LineStart, int? LineEnd);

public static class SourceLineFormatter
{
    public static bool IsFileSource(KnowledgeSource source) => IsFileSource(Parts(source));

    public static bool IsFileSource(SourceParts source) => !string.IsNullOrWhiteSpace(source.FilePath);

    public static SourceParts Parts(KnowledgeSource source) =>
        new(source.FilePath, source.PageNumber, source.Section, source.LineStart, source.LineEnd);

    public static SourceParts Parts(CandidateSource source) =>
        new(source.FilePath, source.PageNumber, null, source.LineStart, source.LineEnd);

    public static string Format(KnowledgeSource source, string fallback) => Format(Parts(source), fallback);

    public static string Format(SourceParts source, string fallback)
    {
        if (IsFileSource(source))
        {
            return FileLine(source.FilePath!, source);
        }

        var parts = new List<string>(2);
        if (source.PageNumber is { } page)
        {
            parts.Add(ReviewStrings.SourcePage(page));
        }

        if (!string.IsNullOrWhiteSpace(source.Section))
        {
            parts.Add(source.Section);
        }

        return parts.Count > 0 ? string.Join(ReviewStrings.SourceJoin, parts) : fallback;
    }

    private static string FileLine(string path, SourceParts source) =>
        source.LineStart is { } first ? ReviewStrings.SourceLines(path, first, source.LineEnd ?? first) : path;
}
